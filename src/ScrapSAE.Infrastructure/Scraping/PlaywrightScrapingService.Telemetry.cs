using Microsoft.Playwright;
using Microsoft.Extensions.Logging;
using ScrapSAE.Core.DTOs;

namespace ScrapSAE.Infrastructure.Scraping;

/// <summary>
/// Partial class con métodos relacionados con telemetría enriquecida
/// </summary>
public partial class PlaywrightScrapingService
{
    /// <summary>
    /// Busca un elemento de forma robusta, intentando múltiples selectores y registrando telemetría en caso de fallo
    /// </summary>
    public async Task<IElementHandle?> FindElementRobustAsync(
        IPage page,
        string selectorKey,
        SiteProfile site,
        string? textToFind = null,
        Guid? executionId = null)
    {
        var execId = executionId ?? Guid.NewGuid();
        
        try
        {
            // Obtener el selector primario del sitio
            string? primarySelector = null;
            if (site.Selectors is Dictionary<string, object> selectors && selectors.ContainsKey(selectorKey))
            {
                primarySelector = selectors[selectorKey]?.ToString();
            }
            
            if (string.IsNullOrEmpty(primarySelector))
            {
                _logger.LogWarning("[FindElementRobust] No se encontró selector primario para clave: {Key}", selectorKey);
                return null;
            }
            
            // Intento 1: Probar selector primario
            var element = await page.QuerySelectorAsync(primarySelector);
            if (element != null)
            {
                _logger.LogDebug("[FindElementRobust] Elemento encontrado con selector primario: {Selector}", primarySelector);
                return element;
            }

            // Intento 2: Probar selectores secundarios del sitio
            if (site.SecondarySelectors.ContainsKey(selectorKey))
            {
                var secondarySelectors = site.SecondarySelectors[selectorKey];
                foreach (var selector in secondarySelectors)
                {
                    element = await page.QuerySelectorAsync(selector);
                    if (element != null)
                    {
                        _logger.LogDebug("[FindElementRobust] Elemento encontrado con selector secundario: {Selector}", selector);
                        return element;
                    }
                }
            }

            // Intento 3: Probar búsqueda por texto
            if (!string.IsNullOrEmpty(textToFind))
            {
                element = await page.QuerySelectorAsync($":text(\"{textToFind}\")");
                if (element != null)
                {
                    _logger.LogDebug("[FindElementRobust] Elemento encontrado por texto: {Text}", textToFind);
                    return element;
                }
            }

            // Si llegamos aquí, todos los intentos fallaron
            // Registrar fallo con telemetría enriquecida
            var secondaryList = site.SecondarySelectors.ContainsKey(selectorKey) 
                ? site.SecondarySelectors[selectorKey] 
                : null;
            await RecordElementNotFoundAsync(page, primarySelector, secondaryList, execId);
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FindElementRobust] Error al buscar elemento: {Key}", selectorKey);
            return null;
        }
    }

    /// <summary>
    /// Registra un fallo de búsqueda de elemento con contexto completo
    /// </summary>
    private async Task RecordElementNotFoundAsync(
        IPage page,
        string primarySelector,
        IEnumerable<string>? secondarySelectors,
        Guid executionId)
    {
        try
        {
            // Capturar snapshot de HTML (área relevante)
            var htmlSnapshot = await CaptureHtmlSnapshotAsync(page);
            
            // Tomar captura de pantalla anotada
            var screenshotPath = await TakeAnnotatedScreenshotAsync(page, primarySelector);
            
            // Crear el paquete de diagnóstico
            var package = new DiagnosticPackage
            {
                ExecutionId = executionId,
                Url = page.Url,
                SelectorAttempted = primarySelector,
                FailureType = "ElementNotFound",
                HtmlSnapshot = htmlSnapshot,
                ScreenshotPath = screenshotPath,
                Annotations = new List<ElementAnnotation>
                {
                    new ElementAnnotation
                    {
                        Selector = primarySelector,
                        Status = "NotFound"
                    }
                }
            };

            // Agregar selectores secundarios a las anotaciones
            if (secondarySelectors != null)
            {
                foreach (var selector in secondarySelectors)
                {
                    package.Annotations.Add(new ElementAnnotation
                    {
                        Selector = selector,
                        Status = "NotFound"
                    });
                }
            }

            // Registrar en el servicio de telemetría
            await _telemetryService.RecordFailureAsync(package);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Telemetry] Error al registrar fallo de elemento");
        }
    }

    /// <summary>
    /// Captura un snapshot del HTML de la página
    /// </summary>
    private async Task<string> CaptureHtmlSnapshotAsync(IPage page)
    {
        try
        {
            var html = await page.ContentAsync();
            
            // Limitar el tamaño del snapshot para evitar archivos muy grandes
            if (html.Length > 50000)
            {
                html = html.Substring(0, 50000) + "\n... (truncado)";
            }
            
            return html;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Telemetry] Error al capturar snapshot de HTML");
            return $"Error al capturar HTML: {ex.Message}";
        }
    }

    /// <summary>
    /// Toma una captura de pantalla y la guarda
    /// </summary>
    private async Task<string> TakeAnnotatedScreenshotAsync(IPage page, string failedSelector)
    {
        try
        {
            var screenshotDir = Path.Combine(Path.GetTempPath(), "scrapsae-telemetry", "screenshots");
            
            if (!Directory.Exists(screenshotDir))
            {
                Directory.CreateDirectory(screenshotDir);
            }
            
            var fileName = $"failure_{Guid.NewGuid()}.png";
            var filePath = Path.Combine(screenshotDir, fileName);
            
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = filePath,
                FullPage = true
            });
            
            _logger.LogDebug("[Telemetry] Captura de pantalla guardada: {Path}", filePath);
            
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Telemetry] Error al tomar captura de pantalla");
            return $"Error: {ex.Message}";
        }
    }
}
