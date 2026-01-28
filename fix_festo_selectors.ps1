$selectors = @{
    scrapingMode              = "families"
    maxPages                  = 1
    productListSelector       = $null  # Importante: Limpiar esto para que no entre en modo tradicional
    categoryUrls              = @("https://www.festo.com/mx/es/c/productos/actuadores/cilindros-neumaticos/cilindros-con-vastago-id_pim215/")
    productFamilyLinkText     = "Explorar la serie"
    productFamilyLinkSelector = "a[href*='/p/']"
    titleSelector             = "[class*='product-name--']"
    imageSelector             = "img[class*='image--']"
    detailSkuSelector         = "span[class*='part-number-value--']"
    detailPriceSelector       = "div[class*='price-display-text--']"
    variantTableSelector      = "div[class*='variants-table-container--']"
    variantRowSelector        = "tr[class*='product-row--']"
    variantSkuLinkSelector    = "a[href*='/p/']"
}

$serviceKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InljanJ0eGtocGpxbGZ2amZidGNuIiwicm9sZSI6InNlcnZpY2Vfcm9sZSIsImlhdCI6MTc2ODk0ODkwNywiZXhwIjoyMDg0NTI0OTA3fQ.1BCSSXRJv-Mbq1ZYMwlcliVRnFJBR4kCpTHcV16Ebyg"
$headers = @{
    "apikey"        = $serviceKey
    "Authorization" = "Bearer $serviceKey"
    "Content-Type"  = "application/json"
    "Prefer"        = "return=representation"
}

$body = @{
    selectors = $selectors
} | ConvertTo-Json -Depth 10

$uri = "https://ycjrtxkhpjqlfvjfbtcn.supabase.co/rest/v1/config_sites?name=eq.Festo"
try {
    $response = Invoke-RestMethod -Uri $uri -Method Patch -Headers $headers -Body $body
    Write-Host "Festo selectors updated successfully! productListSelector is now null."
}
catch {
    Write-Error "Failed to update Festo: $_"
    exit 1
}
