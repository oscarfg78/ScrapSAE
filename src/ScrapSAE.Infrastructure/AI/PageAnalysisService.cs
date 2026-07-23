using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.AI;

/// <summary>
/// Servicio que descarga el HTML de una página con Playwright y lo analiza con GPT
/// para detectar la estructura del catálogo de productos (selectores, campos, estrategias).
/// </summary>
public sealed class PageAnalysisService : IPageAnalysisService, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<PageAnalysisService> _logger;
    private readonly string _model;
    private readonly string? _apiKey;
    private readonly bool _enabled;

    private const int MaxHtmlChars = 50_000;
    private const int AnalysisTimeoutSeconds = 90;

    public PageAnalysisService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<PageAnalysisService> logger)
    {
        _logger = logger;
        // Use a cheaper model for structure analysis; override with OpenAI:AnalysisModel config
        _model = configuration["OpenAI:AnalysisModel"]
               ?? configuration["OpenAI:Model"]
               ?? "gpt-4o-mini";
        _apiKey = configuration["OpenAI:ApiKey"] ?? configuration["OPENAI_API_KEY"];
        _enabled = configuration.GetValue("OpenAI:Enabled", true);

        var baseUrl = configuration["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1/";
        _httpClient = httpClientFactory.CreateClient("OpenAI");
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(120); // overall http timeout; we use CTS for logic timeout
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<PageAnalysisResult> AnalyzeAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("OpenAI no está configurado. Verifica OpenAI:ApiKey y OpenAI:Enabled.");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(AnalysisTimeoutSeconds));

        try
        {
            _logger.LogInformation("[PageAnalysis] Iniciando análisis de: {Url}", url);

            // 1. Download HTML via Playwright
            var (html, pageTitle) = await FetchRenderedHtmlAsync(url, cts.Token);

            var isShopify = html.Contains("window.Shopify", StringComparison.OrdinalIgnoreCase) || 
                            html.Contains("cdn.shopify.com", StringComparison.OrdinalIgnoreCase);

            // 2. Truncate and clean the HTML
            var truncatedHtml = TruncateHtml(html);

            // 3. Send to GPT for analysis
            var result = await AnalyzeWithGptAsync(url, truncatedHtml, pageTitle, cts.Token);
            
            if (isShopify)
            {
                result.StrategyType = "Shopify";
                result.AnalysisSummary += "\n¡Se detectó Shopify! Se utilizará la estrategia nativa /products.json por defecto.";
            }

            _logger.LogInformation("[PageAnalysis] Análisis completado. IsProductCatalog={IsCatalog}, Fields={FieldCount}, Strategy={Strategy}",
                result.IsProductCatalog, result.DetectedFields.Count, result.StrategyType);

            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("[PageAnalysis] Timeout alcanzado al analizar {Url}", url);
            throw new TimeoutException($"El análisis de '{url}' superó el tiempo límite de {AnalysisTimeoutSeconds} segundos.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HTML Fetch via Playwright
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<(string html, string? pageTitle)> FetchRenderedHtmlAsync(
        string url, CancellationToken cancellationToken)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = new[] { "--no-sandbox", "--disable-dev-shm-usage", "--disable-blink-features=AutomationControlled" }
        });

        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 }
        });

        var page = await context.NewPageAsync();

        try
        {
            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 20_000 // 20s for page load
            });
        }
        catch
        {
            // Fallback: try with DOMContentLoaded if networkidle times out
            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 15_000
            });
        }

        // Extra wait to let lazy-loaded content appear
        await page.WaitForTimeoutAsync(2000);

        cancellationToken.ThrowIfCancellationRequested();

        var pageTitle = await page.TitleAsync();
        var html = await page.ContentAsync();

        return (html, pageTitle);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HTML Truncation
    // ─────────────────────────────────────────────────────────────────────────

    private static string TruncateHtml(string fullHtml)
    {
        var parser = new AngleSharp.Html.Parser.HtmlParser();
        var document = parser.ParseDocument(fullHtml);

        // Remove unnecessary tags
        var elementsToRemove = document.QuerySelectorAll("script, style, link, svg, noscript, iframe");
        foreach (var el in elementsToRemove)
        {
            el.Remove();
        }

        // Remove hidden elements
        var hiddenElements = document.QuerySelectorAll("[style*='display: none'], [style*='display:none']");
        foreach (var el in hiddenElements)
        {
            el.Remove();
        }

        var bodyHtml = document.Body?.OuterHtml ?? document.DocumentElement.OuterHtml;

        // Collapse excessive whitespace
        bodyHtml = Regex.Replace(bodyHtml, @"\s{2,}", " ").Trim();

        if (bodyHtml.Length <= MaxHtmlChars)
        {
            return bodyHtml;
        }

        // Try to find the region with highest density of list/grid structures
        var listMatches = Regex.Matches(bodyHtml, @"<(ul|ol|div)[^>]*>", RegexOptions.IgnoreCase);
        if (listMatches.Count > 0)
        {
            var bestStart = 0;
            var bestDensity = 0;
            var windowSize = MaxHtmlChars;

            for (var i = 0; i < listMatches.Count; i++)
            {
                var start = Math.Max(0, listMatches[i].Index - 500);
                var end = Math.Min(bodyHtml.Length, start + windowSize);
                var window = bodyHtml.Substring(start, end - start);
                var density = Regex.Matches(window, @"<(li|div class|article|card)", RegexOptions.IgnoreCase).Count;

                if (density > bestDensity)
                {
                    bestDensity = density;
                    bestStart = start;
                }
            }

            return bodyHtml.Substring(bestStart, Math.Min(windowSize, bodyHtml.Length - bestStart));
        }

        return bodyHtml[..MaxHtmlChars];
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GPT Analysis
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<PageAnalysisResult> AnalyzeWithGptAsync(
        string url, string html, string? pageTitle, CancellationToken cancellationToken)
    {
        var request = BuildAnalysisRequest(url, html, pageTitle);
        using var responseDoc = await SendRequestAsync(request, cancellationToken);
        var outputText = ExtractOutputText(responseDoc);

        if (string.IsNullOrWhiteSpace(outputText))
        {
            throw new InvalidOperationException("GPT no retornó texto de salida para el análisis.");
        }

        // Clean JSON if wrapped in markdown code block
        outputText = CleanJsonOutput(outputText);

        PageAnalysisResult? result;
        try
        {
            result = JsonSerializer.Deserialize<PageAnalysisResult>(outputText, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PageAnalysis] Error al deserializar respuesta de GPT. Texto: {Text}", outputText[..Math.Min(500, outputText.Length)]);
            // Return a minimal result indicating failure to parse
            return new PageAnalysisResult
            {
                AnalyzedUrl = url,
                IsProductCatalog = false,
                AnalysisSummary = "No se pudo interpretar la respuesta del análisis IA.",
                PageTitle = pageTitle
            };
        }

        if (result == null)
        {
            return new PageAnalysisResult
            {
                AnalyzedUrl = url,
                IsProductCatalog = false,
                AnalysisSummary = "El análisis no retornó datos.",
                PageTitle = pageTitle
            };
        }

        result.AnalyzedUrl = url;
        result.AnalyzedAt = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(result.PageTitle) && !string.IsNullOrWhiteSpace(pageTitle))
        {
            result.PageTitle = pageTitle;
        }

        return result;
    }

    private object BuildAnalysisRequest(string url, string html, string? pageTitle)
    {
        var systemPrompt = """
            Eres un experto en análisis de sitios web de comercio electrónico (e-commerce) y scraping web.
            Tu tarea es analizar el HTML proporcionado de una página de catálogo de productos de un proveedor
            y extraer información estructural para configurar un scraper automatizado.

            INSTRUCCIONES:
            1. Determina si la página es un catálogo/listado de productos (isProductCatalog=true) o algo diferente (home, blog, etc.).
            2. Si es un catálogo, identifica los selectores CSS exactos para cada elemento.
            3. Proporciona selectores CSS REALES basados en el HTML analizado, no genéricos.
            4. Evalúa la confianza (high/medium/low) según qué tan claro es el selector en el HTML.
            5. Recomienda la estrategia de scraping más adecuada:
               - "Direct": la página lista los productos directamente
               - "List": hay paginación o listas de categorías que llevan a productos
               - "Families": hay familias/categorías que llevan a sub-listas de productos
            6. Para los selectores secundarios, provee alternativas si el selector principal pudiera fallar.
            7. El campo "analysisSummary" debe ser una descripción breve y clara de lo que encontraste.

            PRIORIDAD de campos a detectar (en orden de importancia):
            1. SKU/código de producto (skuSelector) - CRÍTICO
            2. Nombre del producto (nameSelector) - CRÍTICO
            3. Imagen del producto (imageSelector) - CRÍTICO
            4. Precio (priceSelector) - IMPORTANTE
            5. Características/especificaciones (characteristicsSelector) - IMPORTANTE

            Devuelve SOLO JSON válido con el esquema exacto indicado. Sin markdown, sin explicaciones fuera del JSON.
            """;

        var userPrompt = $"""
            URL analizada: {url}
            Título de la página: {pageTitle ?? "No detectado"}
            
            HTML de la página (puede estar truncado):
            {html}
            """;

        return new
        {
            model = _model,
            temperature = 0.1,
            input = new object[]
            {
                new
                {
                    role = "system",
                    content = new object[] { new { type = "input_text", text = systemPrompt } }
                },
                new
                {
                    role = "user",
                    content = new object[] { new { type = "input_text", text = userPrompt } }
                }
            },
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "page_analysis_result",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[]
                        {
                            "isProductCatalog", "pageTitle", "detectedLanguage",
                            "productContainerSelector", "productCardSelector",
                            "skuSelector", "nameSelector", "imageSelector",
                            "priceSelector", "characteristicsSelector",
                            "secondarySelectors", "recommendedStrategies",
                            "detectedFields", "analysisSummary"
                        },
                        properties = new
                        {
                            isProductCatalog = new { type = "boolean" },
                            pageTitle = new { type = new[] { "string", "null" } },
                            detectedLanguage = new { type = new[] { "string", "null" } },
                            productContainerSelector = new { type = new[] { "string", "null" } },
                            productCardSelector = new { type = new[] { "string", "null" } },
                            skuSelector = new { type = new[] { "string", "null" } },
                            nameSelector = new { type = new[] { "string", "null" } },
                            imageSelector = new { type = new[] { "string", "null" } },
                            priceSelector = new { type = new[] { "string", "null" } },
                            characteristicsSelector = new { type = new[] { "string", "null" } },
                            secondarySelectors = new
                            {
                                type = "object",
                                additionalProperties = false,
                                required = new[] { "sku", "name", "image", "price", "characteristics" },
                                properties = new
                                {
                                    sku = new { type = "array", items = new { type = "string" } },
                                    name = new { type = "array", items = new { type = "string" } },
                                    image = new { type = "array", items = new { type = "string" } },
                                    price = new { type = "array", items = new { type = "string" } },
                                    characteristics = new { type = "array", items = new { type = "string" } }
                                }
                            },
                            recommendedStrategies = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    additionalProperties = false,
                                    required = new[] { "strategyName", "priority", "reason" },
                                    properties = new
                                    {
                                        strategyName = new { type = "string" },
                                        priority = new { type = "integer" },
                                        reason = new { type = new[] { "string", "null" } }
                                    }
                                }
                            },
                            detectedFields = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    additionalProperties = false,
                                    required = new[] { "name", "selector", "confidence", "note" },
                                    properties = new
                                    {
                                        name = new { type = "string" },
                                        selector = new { type = new[] { "string", "null" } },
                                        confidence = new { type = "string", @enum = new[] { "High", "Medium", "Low" } },
                                        note = new { type = new[] { "string", "null" } }
                                    }
                                }
                            },
                            analysisSummary = new { type = new[] { "string", "null" } }
                        }
                    }
                }
            }
        };
    }

    private async Task<JsonDocument> SendRequestAsync(object request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        message.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        message.Content = new StringContent(
            JsonSerializer.Serialize(request, JsonOpts),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("[PageAnalysis] OpenAI request failed: {Status} {Body}", response.StatusCode, body[..Math.Min(500, body.Length)]);
            throw new InvalidOperationException($"Error de OpenAI ({(int)response.StatusCode}): {body[..Math.Min(200, body.Length)]}");
        }

        return JsonDocument.Parse(body);
    }

    private static string? ExtractOutputText(JsonDocument document)
    {
        var root = document.RootElement;

        if (root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString();
        }

        if (!root.TryGetProperty("output", out var outputItems) || outputItems.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in outputItems.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString();
                }
            }
        }

        return null;
    }

    private static string CleanJsonOutput(string output)
    {
        // Remove markdown code fences if present
        var match = Regex.Match(output, @"```(?:json)?\s*([\s\S]*?)```", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : output.Trim();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
