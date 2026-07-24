import sys

file_path = r'c:\Proyectos\ScrapSAE\src\ScrapSAE.Infrastructure\Scraping\PlaywrightScrapingService.cs'
with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

# 1. Update TryScrapeProductDetailWithVariationsAsync signature
content = content.replace(
    'TryScrapeProductDetailWithVariationsAsync(\n        IPage page,\n        string startUrl,\n        SiteSelectors selectors,\n        CancellationToken cancellationToken)',
    'TryScrapeProductDetailWithVariationsAsync(\n        IPage page,\n        string startUrl,\n        SiteSelectors selectors,\n        CancellationToken cancellationToken,\n        Dictionary<string, List<string>>? secondarySelectors = null)'
)
content = content.replace(
    'TryScrapeProductDetailWithVariationsAsync(\n        IPage page,\n        string detailUrl,\n        SiteSelectors selectors,\n        CancellationToken cancellationToken)',
    'TryScrapeProductDetailWithVariationsAsync(\n        IPage page,\n        string detailUrl,\n        SiteSelectors selectors,\n        CancellationToken cancellationToken,\n        Dictionary<string, List<string>>? secondarySelectors = null)'
)


# 2. Update TryScrapeProductDetailWithVariationsAsync calls to ExtractProductFromDetailPageAsync
content = content.replace(
    'var rootProduct = await ExtractProductFromDetailPageAsync(page, selectors, new List<string>());',
    'var rootProduct = await ExtractProductFromDetailPageAsync(page, selectors, new List<string>(), secondarySelectors);'
)
content = content.replace(
    'var variationProduct = await ExtractProductFromDetailPageAsync(page, selectors, new List<string>());',
    'var variationProduct = await ExtractProductFromDetailPageAsync(page, selectors, new List<string>(), secondarySelectors);'
)

# 3. Update ExtractProductFromDetailPageAsync signature
content = content.replace(
    'ExtractProductFromDetailPageAsync(\n        IPage page,\n        SiteSelectors selectors,\n        List<string> categoryPath)',
    'ExtractProductFromDetailPageAsync(\n        IPage page,\n        SiteSelectors selectors,\n        List<string> categoryPath,\n        Dictionary<string, List<string>>? secondarySelectors = null)'
)

# 4. ExtractProductFromDetailPageAsync Body
desc_replacement = '''
            if (descriptionParts.Count > 0)
            {
                product.Description = string.Join(" | ", descriptionParts);
            }

            if (!string.IsNullOrWhiteSpace(selectors.DescriptionSelector))
            {
                var descEl = await page.QuerySelectorAsync(selectors.DescriptionSelector);
                if (descEl != null)
                {
                    var extendedDesc = (await descEl.InnerTextAsync())?.Trim();
                    if (!string.IsNullOrWhiteSpace(extendedDesc))
                    {
                        product.Description = string.IsNullOrWhiteSpace(product.Description) 
                            ? extendedDesc 
                            : $"{product.Description}\\n\\n{extendedDesc}";
                    }
                }
            }

            if (secondarySelectors != null && secondarySelectors.TryGetValue("description", out var secDescSelectors))
            {
                foreach (var sel in secDescSelectors.Where(s => !string.IsNullOrWhiteSpace(s)))
                {
                    var secDescEl = await page.QuerySelectorAsync(sel);
                    if (secDescEl != null)
                    {
                        var text = (await secDescEl.InnerTextAsync())?.Trim();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            product.Description = string.IsNullOrWhiteSpace(product.Description) 
                                ? text 
                                : $"{product.Description}\\n\\n{text}";
                        }
                    }
                }
            }

            var barcode'''
content = content.replace(
    '''
            if (descriptionParts.Count > 0)
            {
                product.Description = string.Join(" | ", descriptionParts);
            }

            var barcode'''.strip('\n'),
    desc_replacement.strip('\n')
)

# 5. ExtractProductFromDetailPageDeepAsync Signature
content = content.replace(
    'ExtractProductFromDetailPageDeepAsync(\n    IPage page,\n    SiteSelectors selectors,\n    Guid siteId,\n    string? familyTitle,\n    CancellationToken cancellationToken)',
    'ExtractProductFromDetailPageDeepAsync(\n    IPage page,\n    SiteSelectors selectors,\n    Guid siteId,\n    string? familyTitle,\n    CancellationToken cancellationToken,\n    Dictionary<string, List<string>>? secondarySelectors = null)'
)

# 6. ExtractProductFromDetailPageDeepAsync Body
deep_desc_replacement = '''
        var descSelectorsToTry = new List<string>();
        if (!string.IsNullOrWhiteSpace(selectors.DetailDescriptionSelector)) descSelectorsToTry.Add(selectors.DetailDescriptionSelector);
        if (!string.IsNullOrWhiteSpace(selectors.DescriptionSelector)) descSelectorsToTry.Add(selectors.DescriptionSelector);
        
        if (secondarySelectors != null && secondarySelectors.TryGetValue("description", out var secDesc) && secDesc != null)
        {
            descSelectorsToTry.AddRange(secDesc.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
        
        descSelectorsToTry.Add(".product-description");
        descSelectorsToTry.Add("[class*='description--']");
        descSelectorsToTry.Add(".description");

        foreach (var sel in descSelectorsToTry)
        {
            var descElem = page.Locator(sel).First;
            if (await descElem.CountAsync() > 0)
            {
                var text = (await descElem.TextContentAsync())?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    product.Description = text;
                    break;
                }
            }
        }
'''
content = content.replace(
    '''
        var descSelector = selectors.DetailDescriptionSelector ?? selectors.DescriptionSelector ?? 
            ".product-description, [class*='description--'], .description";
        var descElem = page.Locator(descSelector).First;
        if (await descElem.CountAsync() > 0)
        {
            product.Description = (await descElem.TextContentAsync())?.Trim();
        }
'''.strip('\n'),
    deep_desc_replacement.strip('\n')
)

# 7. Update calls to TryScrapeProductDetailWithVariationsAsync
content = content.replace(
    'TryScrapeProductDetailWithVariationsAsync(\n                    page,\n                    startUrl,\n                    selectors,\n                    cancellationToken)',
    'TryScrapeProductDetailWithVariationsAsync(\n                    page,\n                    startUrl,\n                    selectors,\n                    cancellationToken,\n                    site.SecondarySelectors)'
)
content = content.replace(
    'TryScrapeProductDetailWithVariationsAsync(page, detailHref, selectors, cancellationToken)',
    'TryScrapeProductDetailWithVariationsAsync(page, detailHref, selectors, cancellationToken, site.SecondarySelectors)'
)

# 8. Update calls to ExtractProductFromDetailPageDeepAsync
content = content.replace(
    'ExtractProductFromDetailPageDeepAsync(\n                            page,\n                            selectors,\n                            siteId,\n                            familyTitle: null,\n                            cancellationToken)',
    'ExtractProductFromDetailPageDeepAsync(\n                            page,\n                            selectors,\n                            siteId,\n                            familyTitle: null,\n                            cancellationToken)'
)
content = content.replace(
    'var product = await ExtractProductFromDetailPageDeepAsync(page, selectors, siteId, familyTitle, cancellationToken);',
    'var product = await ExtractProductFromDetailPageDeepAsync(page, selectors, site.Id, familyTitle, cancellationToken, site.SecondarySelectors);'
)
content = content.replace(
    'ExtractProductFromDetailPageDeepAsync(\n                    page, \n                    selectors, \n                    site.Id, \n                    null);',
    'ExtractProductFromDetailPageDeepAsync(\n                    page, \n                    selectors, \n                    site.Id, \n                    null,\n                    CancellationToken.None,\n                    site.SecondarySelectors);'
)


with open(file_path, 'w', encoding='utf-8') as f:
    f.write(content)

print('Done')
