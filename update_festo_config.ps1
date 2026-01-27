#!/usr/bin/env pwsh
$ErrorActionPreference = "Stop"

$settingsPath = "src\ScrapSAE.Worker\appsettings.json"
if (-not (Test-Path $settingsPath)) {
    throw "No settings found"
}

$settings = Get-Content -Raw -Path $settingsPath | ConvertFrom-Json
$url = $settings.Supabase.Url.TrimEnd('/')
$serviceKey = $settings.Supabase.ServiceKey

$headers = @{
    "apikey"        = $serviceKey
    "Authorization" = "Bearer $serviceKey"
    "Content-Type"  = "application/json"
    "Prefer"        = "return=representation"
}

# Selectors guess
$selectors = @{
    ProductListSelector = ".product-list-item, [data-testid='product-card'], article, .result-item"
    TitleSelector       = "h3, .product-title, [data-testid='product-title']"
    PriceSelector       = ".price, [data-testid='price']"
    SkuSelector         = ".sku, [data-testid='part-number']"
    ImageSelector       = "img"
    NextPageSelector    = ".pagination-next"
    MaxPages            = 1
    UsesInfiniteScroll  = $true
}

$body = @{
    selectors = $selectors
} | ConvertTo-Json -Depth 5

Write-Host "Updating Festo config..."
$response = Invoke-RestMethod -Uri "$url/rest/v1/config_sites?name=eq.Festo" -Method Patch -Headers $headers -Body $body
$response | ConvertTo-Json -Depth 5
