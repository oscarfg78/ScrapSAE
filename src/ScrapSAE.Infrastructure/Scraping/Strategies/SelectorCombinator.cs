using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.Scraping.Strategies;

public static class SelectorCombinator
{
    private static readonly Dictionary<string, string[]> KeyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "productContainer", new[] { "productcontainer", "productlistselector", "productlistclassprefix", "product_container", "container" } },
        { "productCard", new[] { "productcard", "productcardclassprefix", "productlinkselector", "product_card", "card" } },
        { "sku", new[] { "sku", "skuselector", "product_sku" } },
        { "name", new[] { "name", "titleselector", "title" } },
        { "image", new[] { "image", "imageselector", "img" } },
        { "price", new[] { "price", "priceselector", "final_price" } },
        { "characteristics", new[] { "characteristics", "characteristicsselector", "description", "descriptionselector", "detaildescriptionselector" } },
        { "detailLink", new[] { "detaillink", "detailbuttonclassprefix", "productlinkselector", "variantdetaillinkselector" } }
    };

    public static DualSelector? GetDualSelector(SiteProfile? site, string key)
    {
        if (site?.Selectors == null) return null;

        string? rawVal = ExtractRawValue(site.Selectors, key);

        if (string.IsNullOrWhiteSpace(rawVal))
        {
            rawVal = ExtractRawValueFromDto(site.Selectors, key);
        }

        if (string.IsNullOrWhiteSpace(rawVal)) return null;

        return ParseDualSelector(rawVal);
    }

    private static string? ExtractRawValue(object selectors, string key)
    {
        try
        {
            string json = JsonSerializer.Serialize(selectors);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            var targetAliases = GetNormalizedAliases(key);

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                string normProp = NormalizeKeyName(prop.Name);
                foreach (var alias in targetAliases)
                {
                    if (normProp.Equals(alias, StringComparison.OrdinalIgnoreCase))
                    {
                        return prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
                    }
                }
            }
        }
        catch { }

        return null;
    }

    private static string? ExtractRawValueFromDto(object selectors, string key)
    {
        try
        {
            string json = JsonSerializer.Serialize(selectors);
            var dto = JsonSerializer.Deserialize<SiteSelectors>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto == null) return null;

            return key switch
            {
                "productContainer" => dto.ProductListSelector ?? dto.ProductListClassPrefix,
                "productCard" => dto.ProductCardClassPrefix ?? dto.ProductLinkSelector,
                "sku" => dto.SkuSelector,
                "name" => dto.TitleSelector,
                "image" => dto.ImageSelector,
                "price" => dto.PriceSelector,
                "characteristics" => dto.CharacteristicsSelector ?? dto.DescriptionSelector ?? dto.DetailDescriptionSelector,
                "detailLink" => dto.DetailButtonClassPrefix ?? dto.ProductLinkSelector ?? dto.VariantDetailLinkSelector,
                _ => null
            };
        }
        catch { }

        return null;
    }

    private static string[] GetNormalizedAliases(string key)
    {
        if (KeyAliases.TryGetValue(key, out var aliases))
        {
            return aliases;
        }
        return new[] { NormalizeKeyName(key) };
    }

    private static string NormalizeKeyName(string name)
    {
        return Regex.Replace(name, @"[\s_\-]+", "").ToLowerInvariant();
    }

    public static DualSelector ParseDualSelector(string strVal)
    {
        strVal = strVal.Trim();

        if (strVal.StartsWith("{"))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<DualSelector>(strVal, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (parsed != null && (!string.IsNullOrWhiteSpace(parsed.Css) || !string.IsNullOrWhiteSpace(parsed.XPath)))
                {
                    return parsed;
                }
            }
            catch { }
        }

        if (strVal.StartsWith("css=", StringComparison.OrdinalIgnoreCase) || strVal.StartsWith("xpath=", StringComparison.OrdinalIgnoreCase))
        {
            var dual = new DualSelector();
            var xpathIdx = strVal.IndexOf(", xpath=", StringComparison.OrdinalIgnoreCase);

            if (xpathIdx >= 0 && strVal.StartsWith("css=", StringComparison.OrdinalIgnoreCase))
            {
                dual.Css = strVal.Substring(4, xpathIdx - 4).Trim();
                dual.XPath = strVal.Substring(xpathIdx + 8).Trim();
            }
            else
            {
                var cssIdx = strVal.IndexOf(", css=", StringComparison.OrdinalIgnoreCase);
                if (cssIdx >= 0 && strVal.StartsWith("xpath=", StringComparison.OrdinalIgnoreCase))
                {
                    dual.XPath = strVal.Substring(6, cssIdx - 6).Trim();
                    dual.Css = strVal.Substring(cssIdx + 6).Trim();
                }
                else if (strVal.StartsWith("css=", StringComparison.OrdinalIgnoreCase))
                {
                    dual.Css = strVal.Substring(4).Trim();
                }
                else if (strVal.StartsWith("xpath=", StringComparison.OrdinalIgnoreCase))
                {
                    dual.XPath = strVal.Substring(6).Trim();
                }
            }
            return dual;
        }

        if (strVal.StartsWith("//") || strVal.StartsWith("xpath="))
        {
            return new DualSelector { XPath = strVal };
        }

        return new DualSelector { Css = strVal };
    }

    public static List<string> GeneratePermutations(DualSelector selector, bool isPageLevel = false)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(selector.Css))
        {
            var css = selector.Css.Trim();
            candidates.Add(css);

            if (!css.StartsWith(".") && !css.StartsWith("#") && !css.StartsWith("[") && !css.Contains(" ") && !css.Contains(">"))
            {
                candidates.Add($"#{css}");
                candidates.Add($".{css}");
                candidates.Add($"*[id*='{css}']");
                candidates.Add($"*[class*='{css}']");
            }

            if (css.StartsWith(".") && !css.Contains(" ") && !css.Contains(">"))
            {
                string className = css.Substring(1);
                candidates.Add($"div.{className}");
                candidates.Add($"li.{className}");
                candidates.Add($"article.{className}");
                candidates.Add($"*[class*='{className}']");
                
                string xpathClass = isPageLevel
                    ? $"xpath=//*[contains(concat(' ', normalize-space(@class), ' '), ' {className} ')]"
                    : $"xpath=.//*[contains(concat(' ', normalize-space(@class), ' '), ' {className} ')]";
                candidates.Add(xpathClass);
            }
            else if (css.Contains(".media"))
            {
                candidates.Add("modal-opener img");
                candidates.Add("slider-component img");
                candidates.Add(".media img");
                candidates.Add("[class*='media'] img");
            }
        }

        if (!string.IsNullOrWhiteSpace(selector.XPath))
        {
            var xpath = selector.XPath.Trim();
            if (xpath.StartsWith("xpath=")) xpath = xpath.Substring(6).Trim();

            // Convert exact `@class='className'` to `contains(concat(' ', normalize-space(@class), ' '), ' className ')`
            xpath = Regex.Replace(xpath, @"@class\s*=\s*'([^']+)'", "contains(concat(' ', normalize-space(@class), ' '), '$1')");
            xpath = Regex.Replace(xpath, @"@class\s*=\s*""([^""]+)""", "contains(concat(' ', normalize-space(@class), ' '), '$1')");

            // Fix missing `@` in `[id='...']` -> `[@id='...']`
            xpath = Regex.Replace(xpath, @"\[id\s*=", "[@id=");

            // Fix invalid tag-based XPath generated from class names (e.g. .//squama-item -> .//*[contains(@class, 'squama-item')])
            if (!string.IsNullOrWhiteSpace(selector.Css) && selector.Css.StartsWith(".") && !selector.Css.Contains(" "))
            {
                string className = selector.Css.Substring(1);
                if (xpath.EndsWith($"/{className}") || xpath.EndsWith($"//{className}"))
                {
                    xpath = isPageLevel
                        ? $"//*[contains(concat(' ', normalize-space(@class), ' '), ' {className} ')]"
                        : $".//*[contains(concat(' ', normalize-space(@class), ' '), ' {className} ')]";
                }
            }

            if (isPageLevel && xpath.StartsWith(".//"))
            {
                xpath = xpath.Substring(1);
            }

            var formatted = $"xpath={xpath}";
            if (!candidates.Contains(formatted))
            {
                candidates.Add(formatted);
            }
        }

        return candidates;
    }

    public static async Task<IElementHandle?> QuerySelectorResilientAsync(IPage page, DualSelector selector, ScrapingLogTracker? logger = null)
    {
        var permutations = GeneratePermutations(selector, isPageLevel: true);
        foreach (var candidate in permutations)
        {
            try
            {
                var el = await page.QuerySelectorAsync(candidate);
                if (el != null)
                {
                    logger?.AddLog("SelectorCombinator", details: $"QuerySelector exitoso con candidato: {candidate}");
                    return el;
                }
            }
            catch { }
        }
        return null;
    }

    public static async Task<IElementHandle?> QuerySelectorResilientAsync(IElementHandle parent, DualSelector selector, ScrapingLogTracker? logger = null)
    {
        var permutations = GeneratePermutations(selector, isPageLevel: false);
        foreach (var candidate in permutations)
        {
            try
            {
                var el = await parent.QuerySelectorAsync(candidate);
                if (el != null)
                {
                    return el;
                }

                if (!candidate.StartsWith("xpath=") && !candidate.StartsWith("//"))
                {
                    var matches = await parent.EvaluateAsync<bool>("(el, sel) => el.matches(sel)", candidate);
                    if (matches)
                    {
                        return parent;
                    }
                }
            }
            catch { }
        }
        return null;
    }

    public static async Task<IReadOnlyList<IElementHandle>> QuerySelectorAllResilientAsync(IElementHandle parent, DualSelector selector, ScrapingLogTracker? logger = null)
    {
        var permutations = GeneratePermutations(selector, isPageLevel: false);
        foreach (var candidate in permutations)
        {
            try
            {
                var els = await parent.QuerySelectorAllAsync(candidate);
                if (els != null && els.Count > 0)
                {
                    return els;
                }

                if (!candidate.StartsWith("xpath=") && !candidate.StartsWith("//"))
                {
                    var matches = await parent.EvaluateAsync<bool>("(el, sel) => el.matches(sel)", candidate);
                    if (matches)
                    {
                        return new[] { parent };
                    }
                }
            }
            catch { }
        }
        return Array.Empty<IElementHandle>();
    }

    public static async Task<IReadOnlyList<IElementHandle>> QuerySelectorAllResilientAsync(IPage page, DualSelector selector, ScrapingLogTracker? logger = null)
    {
        var permutations = GeneratePermutations(selector, isPageLevel: true);
        foreach (var candidate in permutations)
        {
            try
            {
                var els = await page.QuerySelectorAllAsync(candidate);
                if (els != null && els.Count > 0)
                {
                    logger?.AddLog("SelectorCombinator", details: $"QuerySelectorAll exitoso ({els.Count} elementos) con candidato: {candidate}");
                    return els;
                }
            }
            catch { }
        }
        return Array.Empty<IElementHandle>();
    }

    public static bool IsValidProduct(ScrapedProduct? product)
    {
        if (product == null) return false;
        return !string.IsNullOrWhiteSpace(product.Title) ||
               !string.IsNullOrWhiteSpace(product.SkuSource) ||
               (product.Price.HasValue && product.Price.Value > 0) ||
               !string.IsNullOrWhiteSpace(product.SourceUrl);
    }

    public static bool IsValidDirectProduct(ScrapedProduct? product)
    {
        if (product == null) return false;
        bool hasTitle = !string.IsNullOrWhiteSpace(product.Title);
        bool hasDetails = !string.IsNullOrWhiteSpace(product.SkuSource) ||
                          (product.Price.HasValue && product.Price.Value > 0) ||
                          !string.IsNullOrWhiteSpace(product.ImageUrl) ||
                          !string.IsNullOrWhiteSpace(product.Description);
        return hasTitle && hasDetails;
    }
}
