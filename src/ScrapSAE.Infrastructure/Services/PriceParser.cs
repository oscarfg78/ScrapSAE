using System.Globalization;
using System.Text.RegularExpressions;

namespace ScrapSAE.Infrastructure.Services;

/// <summary>
/// Parser de precios extraídos de texto DOM.
/// Maneja símbolos de moneda (MXN/USD), separadores de miles y decimales europeos.
/// </summary>
public static partial class PriceParser
{
    [GeneratedRegex(@"[\$€£¥₱MXN\s,\.a-zA-Z]+")]
    private static partial Regex CurrencySymbolsRegex();

    /// <summary>
    /// Intenta convertir un texto de precio a decimal.
    /// Retorna null si el texto es nulo, vacío, o no parseable.
    /// </summary>
    /// <remarks>
    /// Ejemplos soportados:
    ///   "$1,234.56"      → 1234.56
    ///   "MXN 1.234,56"  → 1234.56 (formato europeo)
    ///   "1 234.50"      → 1234.50
    ///   "USD 99"        → 99.00
    ///   ""              → null
    /// </remarks>
    public static decimal? TryParse(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return null;

        // 1. Eliminar espacios de no-ruptura y espacios normales alrededor
        var text = rawText.Replace("\u00a0", " ").Trim();

        // 2. Detectar si usa coma como separador decimal (formato europeo: 1.234,56)
        //    Heurística: si la coma aparece después del último punto → es decimal europeo
        var lastCommaIdx = text.LastIndexOf(',');
        var lastDotIdx   = text.LastIndexOf('.');

        bool hasEuropeanDecimal = lastCommaIdx > lastDotIdx && lastCommaIdx > 0 &&
                                  (text.Length - lastCommaIdx - 1) <= 2;

        string normalized;
        if (hasEuropeanDecimal)
        {
            // Eliminar puntos de miles, sustituir coma decimal por punto
            normalized = text.Replace(".", "").Replace(",", ".");
        }
        else
        {
            // Eliminar comas de miles
            normalized = text.Replace(",", "");
        }

        // 3. Quitar símbolos de moneda y texto no numérico (excepto punto y signo -)
        normalized = Regex.Replace(normalized, @"[^\d\.\-]", "");

        // 4. Si quedan varios puntos (error de formato) usar solo el último como decimal
        var parts = normalized.Split('.');
        if (parts.Length > 2)
            normalized = string.Join("", parts[..^1]) + "." + parts[^1];

        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
            return result;

        return null;
    }
}
