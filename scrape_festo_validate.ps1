#!/usr/bin/env pwsh
# Ejecuta el Worker y valida que existan 10 productos de Festo con precio registrado.

param(
    [int]$MaxMinutes = 15,
    [int]$PollSeconds = 20,
    [string]$LogPath = ""
)

$ErrorActionPreference = "Stop"

function Write-Log {
    param([string]$Message)
    Write-Host $Message
    if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
        $Message | Out-File -FilePath $LogPath -Append -Encoding ascii
    }
}

$settingsPath = "src\ScrapSAE.Worker\appsettings.json"
if (-not (Test-Path $settingsPath)) {
    throw "No se encontro el archivo de configuracion: $settingsPath"
}

$settings = Get-Content -Raw -Path $settingsPath | ConvertFrom-Json
$url = $settings.Supabase.Url.TrimEnd('/')
$serviceKey = $settings.Supabase.ServiceKey

if ([string]::IsNullOrWhiteSpace($url) -or [string]::IsNullOrWhiteSpace($serviceKey)) {
    throw "Supabase no esta configurado en $settingsPath"
}

$headers = @{
    "apikey"        = $serviceKey
    "Authorization" = "Bearer $serviceKey"
    "Content-Type"  = "application/json"
}

Write-Log "Obteniendo ID de sitio Festo..."
$festoResponse = Invoke-RestMethod -Uri "$url/rest/v1/config_sites?name=eq.Festo&select=id" -Headers $headers
if (-not $festoResponse -or -not $festoResponse[0].id) {
    throw "No se encontro configuracion de Festo en Supabase."
}
$festoId = $festoResponse[0].id
Write-Log "Festo ID: $festoId"

Write-Log "Iniciando Worker para scraping..."
$worker = Start-Process -FilePath "dotnet" -ArgumentList @(
    "run",
    "--project", "src\ScrapSAE.Worker\ScrapSAE.Worker.csproj"
) -RedirectStandardOutput "worker_debug.log" -RedirectStandardError "worker_debug.err" -PassThru -WindowStyle Hidden

try {
    $deadline = (Get-Date).AddMinutes($MaxMinutes)
    $validated = $false

    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds $PollSeconds

        $products = Invoke-RestMethod -Uri "$url/rest/v1/staging_products?site_id=eq.$festoId&select=sku_source,ai_processed_json,created_at&order=created_at.desc&limit=100" -Headers $headers
        $withPrice = @()

        foreach ($product in $products) {
            if (-not $product.ai_processed_json) {
                continue
            }

            try {
                $json = $product.ai_processed_json | ConvertFrom-Json
            }
            catch {
                continue
            }

            if ($null -ne $json.Price -and $json.Price.ToString().Trim().Length -gt 0) {
                $withPrice += $product
            }
        }

        $count = $withPrice.Count
        Write-Log "Productos con precio detectados: $count"

        if ($count -ge 10) {
            $validated = $true
            break
        }
    }

    if (-not $validated) {
        throw "No se registraron 10 productos con precio antes del timeout."
    }

    Write-Log "Validacion exitosa: 10 productos con precio registrados."
}
finally {
    if ($worker -and -not $worker.HasExited) {
        Write-Log "Deteniendo Worker..."
        Stop-Process -Id $worker.Id -Force
    }
}
