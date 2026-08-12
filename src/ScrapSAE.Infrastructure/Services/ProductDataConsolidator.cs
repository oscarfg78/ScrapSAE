using ScrapSAE.Core.DTOs;

namespace ScrapSAE.Infrastructure.Services;

/// <summary>
/// Consolida los resultados de hasta dos fuentes objetivo en un ConsolidatedProductResult.
/// 
/// Reglas de consolidación:
/// - SupplierCost siempre proviene de la fila Excel original (nunca nulo).
/// - RetailPrice y ImageUrls se toman de la fuente configurada en SourcePriorityConfig.
/// - Si la fuente designada retornó NotFound, se hace fallback a la otra fuente disponible.
/// - Registros donde ambas fuentes son NotFound se emiten con Status=NotMatched (no se descartan).
/// </summary>
public class ProductDataConsolidator
{
    /// <summary>
    /// Consolida una fila Excel con los resultados de hasta dos fuentes objetivo.
    /// </summary>
    public ConsolidatedProductResult Consolidate(
        ExcelProductRecord row,
        TargetScrapeResult r1,
        TargetScrapeResult? r2,
        SourcePriorityConfig priority,
        TargetSearchConfig? targetConfig = null)
    {
        var anyFound = r1.Status == ScrapingResultStatus.Found ||
                       (r2 != null && r2.Status == ScrapingResultStatus.Found);

        var result = new ConsolidatedProductResult
        {
            RowIndex     = row.RowIndex,
            Sku          = row.Sku,
            SupplierCost = row.CostoProveedor,    // SIEMPRE desde Excel
            Status       = anyFound ? ConsolidatedStatus.Matched : ConsolidatedStatus.NotMatched,
            ScrapedAt    = DateTime.UtcNow,
            OptionalAttributes = row.OptionalAttributes
        };

        // ── Precio de venta ─────────────────────────────────────────────────
        var webPrice = ResolvePrice(r1, r2, priority.PriceSource);
        if (webPrice.HasValue && webPrice.Value > 0)
        {
            result.RetailPrice = webPrice;
        }
        else
        {
            // Sin precio web: Intentar cálculo por margen o igualar al costo del proveedor
            decimal? marginPct = row.MarginPercentage ?? targetConfig?.Selectors.DefaultMarginPercentage;

            if (marginPct.HasValue && marginPct.Value > 0)
            {
                // Si el valor es mayor a 1, asumimos que es un porcentaje entero (ej. 30). Si es <= 1, es fracción (ej. 0.30).
                decimal pct = marginPct.Value > 1m ? marginPct.Value / 100m : marginPct.Value;
                result.RetailPrice = Math.Round(row.CostoProveedor + (row.CostoProveedor * pct), 2);
            }
            else
            {
                result.RetailPrice = row.CostoProveedor;
                result.WarningMessage = "⚠️ Precio igualado al costo del proveedor (sin margen configurado)";
            }
        }

        // ── Imágenes ────────────────────────────────────────────────────────
        result.ImageUrls = ResolveImages(r1, r2, priority.ImageSource);

        // ── Título y Descripción (primera fuente que los tenga) ─────────────
        result.Title       = r1.Title ?? r2?.Title;
        result.Description = r1.Description ?? r2?.Description;

        // ── URLs de detalle ─────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(r1.SourceDetailUrl))
            result.SourceDetailUrls.Add(r1.SourceDetailUrl);
        if (r2 != null && !string.IsNullOrWhiteSpace(r2.SourceDetailUrl))
            result.SourceDetailUrls.Add(r2.SourceDetailUrl);

        // ── Atributos / Especificaciones extraídas ────────────────────────────
        foreach (var kvp in r1.OptionalAttributes)
            result.OptionalAttributes[kvp.Key] = kvp.Value;
        if (r2 != null)
        {
            foreach (var kvp in r2.OptionalAttributes)
                result.OptionalAttributes[kvp.Key] = kvp.Value;
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static decimal? ResolvePrice(
        TargetScrapeResult r1,
        TargetScrapeResult? r2,
        DataSource priceSource)
    {
        var primary   = priceSource == DataSource.Target1 ? r1 : r2;
        var secondary = priceSource == DataSource.Target1 ? r2 : r1 as TargetScrapeResult;

        // Fuente designada primero, fallback a la otra si la designada no tiene precio
        return primary?.RetailPrice ?? secondary?.RetailPrice;
    }

    private static List<string> ResolveImages(
        TargetScrapeResult r1,
        TargetScrapeResult? r2,
        DataSource imageSource)
    {
        var primary   = imageSource == DataSource.Target1 ? r1 : r2;
        var secondary = imageSource == DataSource.Target1 ? r2 : r1 as TargetScrapeResult;

        var primaryImages = primary?.ImageUrls ?? new List<string>();
        if (primaryImages.Count > 0) return primaryImages;

        // Fallback a la fuente secundaria si la designada no tiene imágenes
        return secondary?.ImageUrls ?? new List<string>();
    }
}
