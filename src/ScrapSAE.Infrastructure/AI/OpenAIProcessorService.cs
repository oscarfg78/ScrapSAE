using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Http;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.AI;

public sealed class OpenAIProcessorService : IAIProcessorService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAIProcessorService> _logger;
    private readonly string _model;
    private readonly string _visionModel;
    private readonly string? _apiKey;
    private readonly bool _enabled;

    public OpenAIProcessorService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<OpenAIProcessorService> logger)
    {
        _logger = logger;
        _model = configuration["OpenAI:Model"] ?? "gpt-4o-mini";
        _visionModel = configuration["OpenAI:VisionModel"] ?? _model;
        _apiKey = configuration["OpenAI:ApiKey"] ?? configuration["OPENAI_API_KEY"];
        _enabled = configuration.GetValue("OpenAI:Enabled", true);

        var baseUrl = configuration["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1/";
        _httpClient = httpClientFactory.CreateClient("OpenAI");
        _httpClient.BaseAddress = new Uri(baseUrl);
        var timeoutSeconds = configuration.GetValue("OpenAI:TimeoutSeconds", 45);
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds));
    }

    public async Task<ProcessedProduct> ProcessProductAsync(string rawData, CancellationToken cancellationToken = default)
    {
        EnsureEnabled();

        var request = BuildProcessedProductRequest(rawData);
        var responseJson = await SendRequestAsync(request, cancellationToken);
        var outputText = ExtractOutputText(responseJson);
        if (string.IsNullOrWhiteSpace(outputText))
        {
            throw new InvalidOperationException("OpenAI response missing output text.");
        }

        var processed = JsonSerializer.Deserialize<ProcessedProduct>(outputText, JsonOptions);
        if (processed == null)
        {
            throw new InvalidOperationException("Unable to parse OpenAI response.");
        }

        processed.OriginalRawData ??= rawData;
        return processed;
    }

    public async Task<CategorySuggestion> SuggestCategoryAsync(
        string productDescription,
        IEnumerable<ProductLine> availableLines)
    {
        EnsureEnabled();

        var request = BuildCategorySuggestionRequest(productDescription, availableLines);
        var responseJson = await SendRequestAsync(request, CancellationToken.None);
        var outputText = ExtractOutputText(responseJson);
        if (string.IsNullOrWhiteSpace(outputText))
        {
            throw new InvalidOperationException("OpenAI response missing output text.");
        }

        var suggestion = JsonSerializer.Deserialize<CategorySuggestion>(outputText, JsonOptions);
        if (suggestion == null)
        {
            throw new InvalidOperationException("Unable to parse OpenAI response.");
        }

        return suggestion;
    }

    public async Task<SelectorSuggestion> AnalyzeSelectorsAsync(SelectorAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        EnsureEnabled();

        var response = await SendRequestAsync(BuildSelectorAnalysisRequest(request), cancellationToken);
        var outputText = ExtractOutputText(response);
        if (string.IsNullOrWhiteSpace(outputText))
        {
            throw new InvalidOperationException("OpenAI response missing output text.");
        }

        var suggestion = JsonSerializer.Deserialize<SelectorSuggestion>(outputText, JsonOptions);
        if (suggestion == null)
        {
            throw new InvalidOperationException("Unable to parse OpenAI response.");
        }

        return suggestion;
    }

    private void EnsureEnabled()
    {
        if (!_enabled)
        {
            throw new InvalidOperationException("OpenAI processing disabled.");
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("OpenAI API key not configured.");
        }
    }

    private async Task<JsonDocument> SendRequestAsync(object request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("OpenAI request failed: {Status} {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"OpenAI request failed ({(int)response.StatusCode}).");
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

    private object BuildProcessedProductRequest(string rawData)
    {
        var systemPrompt = """
            Eres un experto en extracción de datos de comercio electrónico. Tu tarea es extraer información precisa de productos desde el HTML crudo y/o imágenes proporcionadas.
            
            REGLAS CRÍTICAS:
            1. SKU/PART NUMBER: Identifica el código de artículo ( SKU, Part Number, Order Code). En Festo suele ser una combinación de letras y números (ej. VAMC-L1-CD).
            2. BRAND: Identifica la marca (ej: Festo, Siemens, etc.). Si no estás seguro pero el contexto es de un sitio específico, usa esa marca.
            3. PRECIO: Extrae el valor numérico. Ignora símbolos de moneda pero asegúrate de capturar decimales.
            4. CATEGORÍA: Sugiere la categoría más adecuada basada en el nombre y descripción del producto.
            
            Devuelve SOLO JSON válido que cumpla el esquema. No incluyas explicaciones fuera del JSON.
            Si un campo no se encuentra, usa null o un valor vacío según corresponda, pero prioriza la búsqueda exhaustiva en el HTML.
            """;

        if (TryExtractScreenshot(rawData, out var screenshotBase64, out var sanitizedText))
        {
            return BuildProcessedProductVisionRequest(systemPrompt, sanitizedText, screenshotBase64!);
        }

        var userPrompt = $"Datos crudos del producto:\n{rawData}";

        return new
        {
            model = _model,
            temperature = 0.2,
            input = new object[]
            {
                new
                {
                    role = "system",
                    content = new object[]
                    {
                        new { type = "input_text", text = systemPrompt }
                    }
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_text", text = userPrompt }
                    }
                }
            },
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "processed_product",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[]
                        {
                            "sku",
                            "name",
                            "brand",
                            "model",
                            "description",
                            "features",
                            "specifications",
                            "suggestedCategory",
                            "lineCode",
                            "price",
                            "confidenceScore"
                        },
                        properties = new
                        {
                            sku = new { type = new[] { "string", "null" } },
                            name = new { type = "string" },
                            brand = new { type = new[] { "string", "null" } },
                            model = new { type = new[] { "string", "null" } },
                            description = new { type = "string" },
                            features = new
                            {
                                type = "array",
                                items = new { type = "string" }
                            },
                            specifications = new
                            {
                                type = "object",
                                additionalProperties = new { type = "string" }
                            },
                            suggestedCategory = new { type = new[] { "string", "null" } },
                            lineCode = new { type = new[] { "string", "null" } },
                            price = new { type = new[] { "number", "null" } },
                            confidenceScore = new { type = new[] { "number", "null" }, minimum = 0, maximum = 1 }
                        }
                    }
                }
            }
        };
    }

    private object BuildProcessedProductVisionRequest(string systemPrompt, string textContext, string screenshotBase64)
    {
        var userPrompt = $"Datos del producto (texto):\n{textContext}\n\nAnaliza tambien la imagen adjunta.";

        return new
        {
            model = _visionModel,
            temperature = 0.2,
            input = new object[]
            {
                new
                {
                    role = "system",
                    content = new object[]
                    {
                        new { type = "input_text", text = systemPrompt }
                    }
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_text", text = userPrompt },
                        new { type = "input_image", image_url = $"data:image/png;base64,{screenshotBase64}" }
                    }
                }
            },
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "processed_product",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[]
                        {
                            "sku",
                            "name",
                            "brand",
                            "model",
                            "description",
                            "features",
                            "specifications",
                            "suggestedCategory",
                            "lineCode",
                            "price",
                            "confidenceScore"
                        },
                        properties = new
                        {
                            sku = new { type = new[] { "string", "null" } },
                            name = new { type = "string" },
                            brand = new { type = new[] { "string", "null" } },
                            model = new { type = new[] { "string", "null" } },
                            description = new { type = "string" },
                            features = new { type = "array", items = new { type = "string" } },
                            specifications = new { type = "object", additionalProperties = new { type = "string" } },
                            suggestedCategory = new { type = new[] { "string", "null" } },
                            lineCode = new { type = new[] { "string", "null" } },
                            price = new { type = new[] { "number", "null" } },
                            confidenceScore = new { type = new[] { "number", "null" }, minimum = 0, maximum = 1 }
                        }
                    }
                }
            }
        };
    }

    private static bool TryExtractScreenshot(string rawData, out string? screenshotBase64, out string sanitizedText)
    {
        screenshotBase64 = null;
        sanitizedText = rawData;

        if (string.IsNullOrWhiteSpace(rawData) || !rawData.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawData);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (doc.RootElement.TryGetProperty("screenshotBase64", out var screenshot) &&
                screenshot.ValueKind == JsonValueKind.String)
            {
                screenshotBase64 = screenshot.GetString();
            }

            if (string.IsNullOrWhiteSpace(screenshotBase64))
            {
                return false;
            }

            var sanitized = new Dictionary<string, object?>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, "screenshotBase64", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                sanitized[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.GetDecimal(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Object => prop.Value.ToString(),
                    JsonValueKind.Array => prop.Value.ToString(),
                    _ => null
                };
            }

            sanitizedText = JsonSerializer.Serialize(sanitized, JsonOptions);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private object BuildCategorySuggestionRequest(string productDescription, IEnumerable<ProductLine> availableLines)
    {
        var systemPrompt = """
            Eres un asistente que asigna la linea SAE mas adecuada.
            Usa solo las lineas proporcionadas. Responde en JSON valido segun el esquema.
            """;

        var lines = availableLines
            .Select(line => $"{line.CVE_LIN} - {line.DESC_LIN}")
            .ToArray();

        var userPrompt = $"""
            Producto: {productDescription}
            Lineas disponibles:
            {string.Join("\n", lines)}
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
                    content = new object[]
                    {
                        new { type = "input_text", text = systemPrompt }
                    }
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_text", text = userPrompt }
                    }
                }
            },
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "category_suggestion",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "saeLineCode", "saeLineName", "confidenceScore", "reasoning" },
                        properties = new
                        {
                            saeLineCode = new { type = "string" },
                            saeLineName = new { type = "string" },
                            confidenceScore = new { type = "number", minimum = 0, maximum = 1 },
                            reasoning = new { type = new[] { "string", "null" } }
                        }
                    }
                }
            }
        };
    }

    private object BuildSelectorAnalysisRequest(SelectorAnalysisRequest request)
    {
        var systemPrompt = """
            Eres un asistente experto en scraping. A partir de las imágenes de la página y un fragmento de HTML,
            sugiere selectores y prefijos de clase para extraer productos de forma robusta.
            Devuelve solo JSON válido según el esquema. Si no encuentras algo, usa null.
            """;

        var contentItems = new List<object>
        {
            new { type = "input_text", text = "Analiza la página y sugiere selectores robustos." }
        };

        if (!string.IsNullOrWhiteSpace(request.Url))
        {
            contentItems.Add(new { type = "input_text", text = $"URL: {request.Url}" });
        }

        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            contentItems.Add(new { type = "input_text", text = $"Notas: {request.Notes}" });
        }

        if (!string.IsNullOrWhiteSpace(request.HtmlSnippet))
        {
            contentItems.Add(new { type = "input_text", text = $"HTML snippet:\n{request.HtmlSnippet}" });
        }

        foreach (var imageBase64 in request.ImagesBase64)
        {
            if (string.IsNullOrWhiteSpace(imageBase64))
            {
                continue;
            }

            contentItems.Add(new
            {
                type = "input_image",
                image_url = $"data:image/png;base64,{imageBase64}"
            });
        }

        return new
        {
            model = _visionModel,
            temperature = 0.1,
            input = new object[]
            {
                new
                {
                    role = "system",
                    content = new object[]
                    {
                        new { type = "input_text", text = systemPrompt }
                    }
                },
                new
                {
                    role = "user",
                    content = contentItems.ToArray()
                }
            },
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "selector_suggestion",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[]
                        {
                            "productListClassPrefix",
                            "productCardClassPrefix",
                            "detailButtonText",
                            "detailButtonClassPrefix",
                            "titleSelector",
                            "priceSelector",
                            "skuSelector",
                            "imageSelector",
                            "nextPageSelector",
                            "confidenceScore",
                            "reasoning"
                        },
                        properties = new
                        {
                            productListClassPrefix = new { type = new[] { "string", "null" } },
                            productCardClassPrefix = new { type = new[] { "string", "null" } },
                            detailButtonText = new { type = new[] { "string", "null" } },
                            detailButtonClassPrefix = new { type = new[] { "string", "null" } },
                            titleSelector = new { type = new[] { "string", "null" } },
                            priceSelector = new { type = new[] { "string", "null" } },
                            skuSelector = new { type = new[] { "string", "null" } },
                            imageSelector = new { type = new[] { "string", "null" } },
                            nextPageSelector = new { type = new[] { "string", "null" } },
                            confidenceScore = new { type = new[] { "number", "null" }, minimum = 0, maximum = 1 },
                            reasoning = new { type = new[] { "string", "null" } }
                        }
                    }
                }
            }
        };
    }
}
