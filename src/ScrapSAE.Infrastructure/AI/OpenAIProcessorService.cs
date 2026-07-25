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
            1. SKU/PART NUMBER: Identifica el código de artículo (SKU, Part Number, Order Code). En Festo suele ser una combinación de letras y números (ej. VAMC-L1-CD).
            2. BRAND: Identifica la marca (ej: Festo, Siemens, etc.). Si no estás seguro pero el contexto es de un sitio específico, usa esa marca.
            3. PRECIO: Extrae el valor numérico. Ignora símbolos de moneda pero asegúrate de capturar decimales.
            4. MONEDA: Identifica la moneda del precio (USD, MXN, EUR, etc.). Si no está explícita, infiere del contexto del sitio.
            5. CATEGORÍAS: Sugiere todas las categorías relevantes basadas en el nombre y descripción del producto (puede ser más de una).
            6. GALERÍA DE IMÁGENES: Extrae TODAS las URLs de las imágenes del producto, no solo la principal. Busca elementos como galerías, thumbnails, imágenes alternativas.
            7. STOCK/INVENTARIO: Busca indicadores de stock, cantidad disponible, o estado de disponibilidad. Extrae el valor numérico si está presente.
            8. ARCHIVOS ADJUNTOS: Identifica enlaces a documentos PDF, fichas técnicas, manuales de usuario, catálogos. Extrae la URL y el nombre del archivo.
            9. ESPECIFICACIONES: Extrae todas las especificaciones técnicas en formato clave-valor (dimensiones, peso, material, certificaciones, etc.). Si los datos crudos incluyen un campo 'CharacteristicsHtml', utilízalo como la fuente principal para extraer las especificaciones y características, mapeando tablas o listas complejas a la lista de 'Features' o al diccionario de 'Specifications'.
            10. DESCRIPCIÓN: Extrae la descripción extendida del producto. Si los datos crudos incluyen información en la propiedad "Description", asegúrate de mantenerla o enriquecerla.
            
            Devuelve SOLO JSON válido que cumpla el esquema. No incluyas explicaciones fuera del JSON.
            Si un campo no se encuentra, usa null o un array vacío según corresponda, pero prioriza la búsqueda exhaustiva en el HTML.
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
                    strict = false,
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
                            "categories",
                            "lineCode",
                            "price",
                            "currency",
                            "stock",
                            "images",
                            "attachments",
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
                            categories = new
                            {
                                type = "array",
                                items = new { type = "string" }
                            },
                            lineCode = new { type = new[] { "string", "null" } },
                            price = new { type = new[] { "number", "null" } },
                            currency = new { type = new[] { "string", "null" } },
                            stock = new { type = new[] { "integer", "null" } },
                            images = new
                            {
                                type = "array",
                                items = new { type = "string" }
                            },
                            attachments = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    required = new[] { "fileName", "fileUrl" },
                                    properties = new
                                    {
                                        fileName = new { type = "string" },
                                        fileUrl = new { type = "string" },
                                        fileType = new { type = new[] { "string", "null" } },
                                        fileSizeBytes = new { type = new[] { "integer", "null" } }
                                    }
                                }
                            },
                            confidenceScore = new { type = new[] { "number", "null" }, minimum = 0, maximum = 1 }
                        }
                    }
                }
            }
        };
    }

    private object BuildProcessedProductVisionRequest(string systemPrompt, string textContext, string screenshotBase64)
    {
        var userPrompt = $"Datos del producto (texto):\n{textContext}\n\nAnaliza también la imagen adjunta para extraer información adicional.";

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
                        new { type = "input_image", source = new { type = "base64", media_type = "image/png", data = screenshotBase64 } }
                    }
                }
            },
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "processed_product",
                    strict = false,
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
                            "categories",
                            "lineCode",
                            "price",
                            "currency",
                            "stock",
                            "images",
                            "attachments",
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
                            categories = new
                            {
                                type = "array",
                                items = new { type = "string" }
                            },
                            lineCode = new { type = new[] { "string", "null" } },
                            price = new { type = new[] { "number", "null" } },
                            currency = new { type = new[] { "string", "null" } },
                            stock = new { type = new[] { "integer", "null" } },
                            images = new
                            {
                                type = "array",
                                items = new { type = "string" }
                            },
                            attachments = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    required = new[] { "fileName", "fileUrl" },
                                    properties = new
                                    {
                                        fileName = new { type = "string" },
                                        fileUrl = new { type = "string" },
                                        fileType = new { type = new[] { "string", "null" } },
                                        fileSizeBytes = new { type = new[] { "integer", "null" } }
                                    }
                                }
                            },
                            confidenceScore = new { type = new[] { "number", "null" }, minimum = 0, maximum = 1 }
                        }
                    }
                }
            }
        };
    }

    private static bool TryExtractScreenshot(string rawData, out string? screenshotBase64, out string? sanitizedText)
    {
        screenshotBase64 = null;
        sanitizedText = null;

        const string marker = "SCREENSHOT_BASE64:";
        var index = rawData.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return false;
        }

        var endIndex = rawData.IndexOf('\n', index);
        if (endIndex < 0)
        {
            endIndex = rawData.Length;
        }

        screenshotBase64 = rawData.Substring(index + marker.Length, endIndex - index - marker.Length).Trim();
        sanitizedText = rawData.Substring(0, index) + (endIndex < rawData.Length ? rawData.Substring(endIndex) : string.Empty);

        return !string.IsNullOrWhiteSpace(screenshotBase64);
    }

    private object BuildCategorySuggestionRequest(string productDescription, IEnumerable<ProductLine> availableLines)
    {
        var linesText = string.Join("\n", availableLines.Select(l => $"- {l.CVE_LIN}: {l.DESC_LIN}"));

        var systemPrompt = $"""
            Eres un experto en clasificación de productos industriales. Tu tarea es sugerir la línea de producto más adecuada para un artículo dado.
            
            Líneas disponibles:
            {linesText}
            
            Devuelve SOLO JSON válido con la línea sugerida, un nivel de confianza (0-1) y una breve justificación.
            """;

        var userPrompt = $"Producto a clasificar:\n{productDescription}";

        return new
        {
            model = _model,
            temperature = 0.3,
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
            Eres un experto en web scraping y análisis de HTML. Tu tarea es analizar el HTML de una página de e-commerce y sugerir selectores óptimos para extraer información de productos.
            
            Debes sugerir TANTO selectores CSS como expresiones XPath para cada campo, devolviendo un objeto `{ "css": "...", "xpath": "..." }`:
            - Usa selectores CSS (ej. `.product-card`, `#price`) si los elementos tienen clases o IDs claros.
            - Usa XPath (ej. `//div[@id="tab-content"]/p[1]`, o `//td[contains(text(), "Price")]/following-sibling::td`) si es más fácil acceder a elementos sin clases consistentes o que dependan de texto y relaciones familiares.
            
            IMPORTANTE: Si el selector que sugieres es un XPath, ASEGÚRATE de que inicie con `//` o `xpath=` para que el sistema lo procese correctamente. Si es CSS, asegúrate de que use los prefijos correctos (`.`, `#`). Ambos son obligatorios (si uno no aplica, ponlo null, pero intenta llenar ambos).
            
            Busca patrones comunes como:
            - Clases o XPaths consistentes para listas y tarjetas de productos
            - Selectores para botones de detalle
            - Selectores para información del producto (título, precio, SKU, imagen)
            - Selectores para paginación
            
            Devuelve SOLO JSON válido con los objetos de selectores sugeridos y un nivel de confianza.
            """;

        var userPrompt = $"URL: {request.Url}\n\nHTML snippet:\n{request.HtmlSnippet}\n\nNotas: {request.Notes}";

        var content = new List<object>
        {
            new { type = "input_text", text = userPrompt }
        };

        if (request.ImagesBase64 != null && request.ImagesBase64.Any())
        {
            foreach (var imgBase64 in request.ImagesBase64.Take(3))
            {
                content.Add(new { type = "input_image", source = new { type = "base64", media_type = "image/png", data = imgBase64 } });
            }
        }

        return new
        {
            model = request.ImagesBase64?.Any() == true ? _visionModel : _model,
            temperature = 0.3,
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
                    content = content.ToArray()
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
                        required = new[] { "confidenceScore" },
                        properties = new
                        {
                            productListClassPrefix = new { type = new[] { "object", "null" }, properties = new { css = new { type = new[] { "string", "null" } }, xpath = new { type = new[] { "string", "null" } } } },
                            productCardClassPrefix = new { type = new[] { "object", "null" }, properties = new { css = new { type = new[] { "string", "null" } }, xpath = new { type = new[] { "string", "null" } } } },
                            detailButtonText = new { type = new[] { "object", "null" }, properties = new { css = new { type = new[] { "string", "null" } }, xpath = new { type = new[] { "string", "null" } } } },
                            detailButtonClassPrefix = new { type = new[] { "object", "null" }, properties = new { css = new { type = new[] { "string", "null" } }, xpath = new { type = new[] { "string", "null" } } } },
                            titleSelector = new { type = new[] { "object", "null" }, properties = new { css = new { type = new[] { "string", "null" } }, xpath = new { type = new[] { "string", "null" } } } },
                            priceSelector = new { type = new[] { "object", "null" }, properties = new { css = new { type = new[] { "string", "null" } }, xpath = new { type = new[] { "string", "null" } } } },
                            skuSelector = new { type = new[] { "object", "null" }, properties = new { css = new { type = new[] { "string", "null" } }, xpath = new { type = new[] { "string", "null" } } } },
                            imageSelector = new { type = new[] { "object", "null" }, properties = new { css = new { type = new[] { "string", "null" } }, xpath = new { type = new[] { "string", "null" } } } },
                            nextPageSelector = new { type = new[] { "object", "null" }, properties = new { css = new { type = new[] { "string", "null" } }, xpath = new { type = new[] { "string", "null" } } } },
                            confidenceScore = new { type = "number", minimum = 0, maximum = 1 },
                            reasoning = new { type = new[] { "string", "null" } }
                        }
                    }
                }
            }
        };
    }
}
