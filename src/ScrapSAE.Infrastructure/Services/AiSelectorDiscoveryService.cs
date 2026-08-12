using System.Text.Json;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.Services;

/// <summary>
/// Servicio que usa la API de análisis IA (existente) para inferir selectores CSS/XPath
/// de una página objetivo durante la configuración del Concurrent Wizard.
///
/// IMPORTANTE: Este servicio SOLO debe ser inyectado en ViewModels de configuración (Steps 1-3).
/// El ConcurrentScrapingEngine NO debe depender de ISelectorDiscoveryService.
/// </summary>
public class AiSelectorDiscoveryService : ISelectorDiscoveryService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiBaseUrl;

    // Tamaño máximo del HTML enviado a la IA (50 KB)
    private const int MaxHtmlSizeBytes = 50 * 1024;

    public AiSelectorDiscoveryService(HttpClient httpClient, string apiBaseUrl)
    {
        _httpClient = httpClient;
        _apiBaseUrl = apiBaseUrl.TrimEnd('/');
    }

    /// <inheritdoc/>
    public async Task<SelectorConfig> DiscoverSelectorsAsync(
        string targetUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Fetch HTML de la URL objetivo (ligero, solo estructura DOM)
            var pageHtml = await FetchPageHtmlAsync(targetUrl, cancellationToken);
            if (string.IsNullOrWhiteSpace(pageHtml))
                return new SelectorConfig();

            // 2. Truncar al tamaño máximo
            var truncatedHtml = TruncateHtml(pageHtml, MaxHtmlSizeBytes);

            // 3. Llamar al endpoint de análisis IA existente
            var analysisResult = await InvokeAnalysisApiAsync(targetUrl, truncatedHtml, cancellationToken);
            if (analysisResult == null)
                return new SelectorConfig();

            // 4. Mapear el resultado al SelectorConfig del wizard concurrente
            return MapToSelectorConfig(analysisResult);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Fallo silencioso: devuelve config vacía, el UI muestra el error
            return new SelectorConfig();
        }
    }

    private async Task<string?> FetchPageHtmlAsync(string url, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cts.Token);
    }

    private static string TruncateHtml(string html, int maxBytes)
    {
        var bytes = System.Text.Encoding.UTF8.GetByteCount(html);
        if (bytes <= maxBytes) return html;

        // Buscar el cierre del body para truncar limpiamente
        var bodyStart = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        if (bodyStart < 0) bodyStart = 0;

        var targetChars = maxBytes / 3; // Estimación conservadora UTF-8
        var end = Math.Min(bodyStart + targetChars, html.Length);
        return html[..end];
    }

    private async Task<PageAnalysisApiResponse?> InvokeAnalysisApiAsync(
        string targetUrl,
        string html,
        CancellationToken cancellationToken)
    {
        // Reutiliza el endpoint /api/analysis/analyze-page (existente en ScrapSAE.Api)
        // con un contexto especial que le pide devolver solo SelectorConfig
        var payload = new
        {
            url = targetUrl,
            htmlSnippet = html,
            mode = "selector_discovery",
            context = "Concurrent Scraping Wizard: identify search input, submit button, first result card, detail link, retail price, and image gallery selectors."
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(90));

        var response = await _httpClient.PostAsync(
            $"{_apiBaseUrl}/api/analysis/analyze-page",
            content,
            cts.Token);

        if (!response.IsSuccessStatusCode) return null;

        var responseJson = await response.Content.ReadAsStringAsync(cts.Token);
        return JsonSerializer.Deserialize<PageAnalysisApiResponse>(responseJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private static SelectorConfig MapToSelectorConfig(PageAnalysisApiResponse result)
    {
        // Mapea los campos de PageAnalysisResult (existente) a SelectorConfig del wizard
        return new SelectorConfig
        {
            SearchInputSelector  = result.SearchInputSelector,
            SearchSubmitSelector = result.SearchButtonSelector,
            FirstResultCardSelector = result.ProductCardSelector ?? result.ProductListSelector,
            DetailLinkSelector   = result.ProductLinkSelector,
            RetailPriceSelector  = result.PriceSelector,
            ImageGallerySelector = result.ImageGallerySelector ?? result.ImageSelector,
            TitleSelector        = result.TitleSelector,
            DescriptionSelector  = result.DescriptionSelector,
            AttributesSelector   = result.AttributesSelector
        };
    }

    // DTO interno para deserializar la respuesta del API existente
    private class PageAnalysisApiResponse
    {
        public string? SearchInputSelector { get; set; }
        public string? SearchButtonSelector { get; set; }
        public string? ProductListSelector { get; set; }
        public string? ProductCardSelector { get; set; }
        public string? ProductLinkSelector { get; set; }
        public string? PriceSelector { get; set; }
        public string? ImageSelector { get; set; }
        public string? ImageGallerySelector { get; set; }
        public string? TitleSelector { get; set; }
        public string? DescriptionSelector { get; set; }
        public string? AttributesSelector { get; set; }
    }
}

// Importación requerida para PageAnalysisResult (ya existe en Core)
internal class PageAnalysisApiResponse { }
