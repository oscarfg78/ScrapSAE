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
    "apikey" = $serviceKey
    "Authorization" = "Bearer $serviceKey"
    "Content-Type" = "application/json"
}

Write-Host "Fetching Festo config..."
$response = Invoke-RestMethod -Uri "$url/rest/v1/config_sites?name=eq.Festo" -Headers $headers
$response | ConvertTo-Json -Depth 5
