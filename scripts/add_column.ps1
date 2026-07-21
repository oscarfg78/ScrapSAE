# Script para agregar columna a Supabase
$url = "https://ycjrtxkhpjqlfvjfbtcn.supabase.co/rest/v1/rpc/exec_sql"
$serviceKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InljanJ0eGtocGpxbGZ2amZidGNuIiwicm9sZSI6InNlcnZpY2Vfcm9sZSIsImlhdCI6MTc2ODk0ODkwNywiZXhwIjoyMDg0NTI0OTA3fQ.1BCSSXRJv-Mbq1ZYMwlcliVRnFJBR4kCpTHcV16Ebyg"

$headers = @{
    "apikey" = $serviceKey
    "Authorization" = "Bearer $serviceKey"
    "Content-Type" = "application/json"
}

$body = @{
    query = @"
ALTER TABLE config_sites ADD COLUMN IF NOT EXISTS max_products_per_scrape INT DEFAULT 0;
ALTER TABLE config_sites ADD COLUMN IF NOT EXISTS login_url TEXT;
"@
} | ConvertTo-Json

$response = Invoke-RestMethod -Uri $url -Method Post -Headers $headers -Body $body
Write-Host "Response: $response"
