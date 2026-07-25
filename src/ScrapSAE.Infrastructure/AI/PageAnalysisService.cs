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

    public async Task<PageAnalysisResult> AnalyzeAsync(string catalogUrl, string? productDetailUrl = null, CancellationToken cancellationToken = default)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("OpenAI no está configurado. Verifica OpenAI:ApiKey y OpenAI:Enabled.");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(AnalysisTimeoutSeconds));

        try
        {
            _logger.LogInformation("[PageAnalysis] Iniciando análisis de: {Url}", catalogUrl);

            // 1. Download HTML via Playwright
            var (html, pageTitle) = await FetchRenderedHtmlAsync(catalogUrl, cts.Token);

            var isShopify = html.Contains("window.Shopify", StringComparison.OrdinalIgnoreCase) || 
                            html.Contains("cdn.shopify.com", StringComparison.OrdinalIgnoreCase);

            // 2. Truncate and clean the HTML
            var truncatedHtml = ExtractDomSkeleton(html);

            string? truncatedProductDetailHtml = null;
            if (!string.IsNullOrWhiteSpace(productDetailUrl))
            {
                _logger.LogInformation("[PageAnalysis] Descargando HTML de detalle de producto: {Url}", productDetailUrl);
                var (detailHtml, _) = await FetchRenderedHtmlAsync(productDetailUrl, cts.Token);
                truncatedProductDetailHtml = ExtractDomSkeleton(detailHtml);
            }
            else
            {
                // Attempt to discover a candidate link
                var candidateLink = FindCandidateProductLink(html, catalogUrl);
                if (candidateLink != null)
                {
                    _logger.LogInformation("[PageAnalysis] Descubierto enlace candidato: {Url}", candidateLink);
                    var (detailHtml, _) = await FetchRenderedHtmlAsync(candidateLink, cts.Token);
                    truncatedProductDetailHtml = ExtractDomSkeleton(detailHtml);
                    productDetailUrl = candidateLink; // Use it for the result
                }
            }

            // 3. Send to GPT for analysis
            var result = await AnalyzeWithGptAsync(catalogUrl, productDetailUrl, truncatedHtml, truncatedProductDetailHtml, pageTitle, cts.Token);
            result.CandidateDetailUrl = productDetailUrl;
            
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
            _logger.LogWarning("[PageAnalysis] Timeout alcanzado al analizar {Url}", catalogUrl);
            throw new TimeoutException($"El análisis de '{catalogUrl}' superó el tiempo límite de {AnalysisTimeoutSeconds} segundos.");
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
    // HTML Truncation and DOM Skeleton Extraction
    // ─────────────────────────────────────────────────────────────────────────

    private static string ExtractDomSkeleton(string fullHtml)
    {
        var parser = new AngleSharp.Html.Parser.HtmlParser();
        var document = parser.ParseDocument(fullHtml);

        // 1. Remove unnecessary tags completely
        var elementsToRemove = document.QuerySelectorAll("script, style, link, svg, noscript, iframe, meta, head, footer, header, nav, path");
        foreach (var el in elementsToRemove)
        {
            el.Remove();
        }

        // 2. Remove hidden elements
        var hiddenElements = document.QuerySelectorAll("[style*='display: none'], [style*='display:none']");
        foreach (var el in hiddenElements)
        {
            el.Remove();
        }

        // 3. Clean attributes to reduce noise, keep only semantic ones
        var allElements = document.QuerySelectorAll("*");
        var allowedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "class", "id", "href", "src", "alt", "itemprop" };
        foreach (var el in allElements)
        {
            var attributesToRemove = el.Attributes
                .Select(a => a.Name)
                .Where(name => !allowedAttributes.Contains(name) && !name.StartsWith("data-"))
                .ToList();
            
            foreach (var attr in attributesToRemove)
            {
                el.RemoveAttribute(attr);
            }
        }

        var bodyHtml = document.Body?.OuterHtml ?? document.DocumentElement.OuterHtml;

        // Collapse excessive whitespace
        bodyHtml = Regex.Replace(bodyHtml, @"\s{2,}", " ").Trim();

        if (bodyHtml.Length <= MaxHtmlChars)
        {
            return bodyHtml;
        }

        // Try to find the region with highest density of lists or semantic containers
        var listMatches = Regex.Matches(bodyHtml, @"<(ul|ol|div|table)[^>]*>", RegexOptions.IgnoreCase);
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
                var density = Regex.Matches(window, @"<(li|div class|article|card|tr|td|a href)", RegexOptions.IgnoreCase).Count;

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

    private static string? FindCandidateProductLink(string catalogHtml, string baseUrl)
    {
        var parser = new AngleSharp.Html.Parser.HtmlParser();
        var document = parser.ParseDocument(catalogHtml);
        var baseUri = new Uri(baseUrl);

        // Find links that might be product details (e.g., have an image inside, or have long hrefs, or specific paths)
        var links = document.QuerySelectorAll("a[href]");
        
        foreach (var link in links)
        {
            var href = link.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href) || href.StartsWith("#") || href.StartsWith("javascript:")) continue;

            // Heuristic: A product link usually has an image inside or class indicating product
            if (link.QuerySelector("img") != null || (link.ClassName != null && link.ClassName.Contains("product", StringComparison.OrdinalIgnoreCase)))
            {
                // Ensure it's not a generic category link
                if (href.Contains("category", StringComparison.OrdinalIgnoreCase) || href.Contains("collection", StringComparison.OrdinalIgnoreCase) && !href.Contains("product", StringComparison.OrdinalIgnoreCase)) 
                    continue;

                try 
                {
                    var absoluteUri = new Uri(baseUri, href);
                    // Filter out external links
                    if (absoluteUri.Host == baseUri.Host)
                    {
                        return absoluteUri.ToString();
                    }
                }
                catch { }
            }
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GPT Analysis
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<PageAnalysisResult> AnalyzeWithGptAsync(
        string url, string? productDetailUrl, string html, string? productDetailHtml, string? pageTitle, CancellationToken cancellationToken)
    {
        var request = BuildAnalysisRequest(url, productDetailUrl, html, productDetailHtml, pageTitle);
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

    private object BuildAnalysisRequest(string url, string? productDetailUrl, string html, string? productDetailHtml, string? pageTitle)
    {
        var systemPrompt = """
            Eres un experto en análisis de sitios web de comercio electrónico y automatización con Playwright.
            Tu tarea es analizar un "DOM Skeleton" (HTML simplificado con AngleSharp) y extraer los selectores óptimos.

            INSTRUCCIONES:
            1. Determina si la página es un catálogo (isProductCatalog=true).
            2. Genera selectores CSS y XPath robustos, priorizando clases semánticas e IDs.
            3. CRÍTICO PARA XPATH: Evita rutas absolutas largas. Usa siempre rutas relativas como `//div[@class='precio']` o `.//span`.
            4. CRÍTICO PARA CSS: Evita selectores anidados profundos. Usa selectores claros como `.product-price` o `[data-sku]`.
            5. Evalúa la confianza (high/medium/low) según qué tan robustos son los selectores.
            6. Recomienda estrategia: "Direct", "List", o "Families".

            PRIORIDAD de campos a detectar:
            1. Contenedor de la lista y tarjeta (productContainerSelector, productCardSelector)
            2. SKU, Nombre, Imagen, Precio, Características.
            
            Usa la información del HTML principal y, si se provee, la del Detalle de Producto.
            Devuelve SOLO JSON válido con el esquema exacto indicado. Sin explicaciones.
            """;

        var userPrompt = $"""
            URL analizada: {url}
            Título de la página: {pageTitle ?? "No detectado"}
            
            HTML de la página (puede estar truncado):
            {html}
            """;

        if (!string.IsNullOrWhiteSpace(productDetailHtml))
        {
            userPrompt += $"\n\nHTML de Detalle de Producto ({productDetailUrl}) (usar para extraer selectores de detalle):\n{productDetailHtml}";
        }

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
                            productContainerSelector = new { type = new[] { "object", "null" }, additionalProperties = false, required = new[] { "css", "xpath" }, properties = new { css = new { type = new[] { "string", "null" } }, xpath = new { type = new[] { "string", "null" } } } },
                            productCardSelector = new { type = new[] { "object", "null" }, additionalProperties = false, required = new[] { "css", "xpath" }, properties = new { css = new { type = new[] { "string", "null" } }, xpath = new { type = new[] { "string", "null" } } } },
                            skuSelector = new { type = new[] { "object", "null" }, additionalProperties = false, required = new[] { "css", "xpath" }, properties = new { css = new { type = new[] { "string", "null" } }, xpath = new { type = new[] { "string", "null" } } } },
                            nameSelector = new { type = new[] { "object", "null" }, additionalProperties = false, required = new[] { "css", "xpath" }, properties = new { css = new { type = new[] { "string", "null" } }, xpath = new { type = new[] { "string", "null" } } } },
                            imageSelector = new { type = new[] { "object", "null" }, additionalProperties = false, required = new[] { "css", "xpath" }, properties = new { css = new { type = new[] { "string", "null" } }, xpath = new { type = new[] { "string", "null" } } } },
                            priceSelector = new { type = new[] { "object", "null" }, additionalProperties = false, required = new[] { "css", "xpath" }, properties = new { css = new { type = new[] { "string", "null" } }, xpath = new { type = new[] { "string", "null" } } } },
                            characteristicsSelector = new { type = new[] { "object", "null" }, additionalProperties = false, required = new[] { "css", "xpath" }, properties = new { css = new { type = new[] { "string", "null" } }, xpath = new { type = new[] { "string", "null" } } } },
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
