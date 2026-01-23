#!/usr/bin/env pwsh
# Script para validar productos de Festo en Supabase

$url = "https://ycjrtxkhpjqlfvjfbtcn.supabase.co"
$serviceKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InljanJ0eGtocGpxbGZ2amZidGNuIiwicm9sZSI6InNlcnZpY2Vfcm9sZSIsImlhdCI6MTc2ODk0ODkwNywiZXhwIjoyMDg0NTI0OTA3fQ.1BCSSXRJv-Mbq1ZYMwlcliVRnFJBR4kCpTHcV16Ebyg"

$headers = @{
    "apikey" = $serviceKey
    "Authorization" = "Bearer $serviceKey"
    "Content-Type" = "application/json"
}

# Get Festo site ID
Write-Host "Obteniendo ID de sitio Festo..."
$festoResponse = Invoke-RestMethod -Uri "$url/rest/v1/config_sites?name=eq.Festo&select=id" -Headers $headers
$festoId = $festoResponse[0].id
Write-Host "Festo ID: $festoId"

# Get products count from Festo
Write-Host "`nObteniendo cantidad de productos de Festo..."
$productsUrl = "$url/rest/v1/staging_products?site_id=eq.$festoId&select=id,sku_source,status,created_at"
$productsResponse = Invoke-RestMethod -Uri $productsUrl -Headers $headers

Write-Host "Total productos traídos de Festo: $($productsResponse.Count)"
Write-Host ""
Write-Host "Productos:"
$productsResponse | ForEach-Object {
    Write-Host "  - SKU: $($_.sku_source), Status: $($_.status), Creado: $($_.created_at)"
}

if ($productsResponse.Count -eq 10) {
    Write-Host "`n✅ ÉXITO: Se obtuvieron exactamente 10 productos de Festo (límite aplicado correctamente)"
} else {
    Write-Host "`n⚠️ Se obtuvieron $($productsResponse.Count) productos (esperados: 10)"
}
