param([int]$DurationMinutes = 120)

$ErrorActionPreference = "Stop"
$runId = Get-Date -Format "yyyyMMdd-HHmmss"
$stateDirectory = Join-Path $env:TEMP "hrp-soak-$runId"
$metricsPath = Join-Path $env:TEMP "hrp-soak-$runId.metrics.jsonl"
$apiOutput = Join-Path $env:TEMP "hrp-soak-$runId.api.out.log"
$apiError = Join-Path $env:TEMP "hrp-soak-$runId.api.err.log"
$soakOutput = Join-Path $env:TEMP "hrp-soak-$runId.runner.out.log"
$soakError = Join-Path $env:TEMP "hrp-soak-$runId.runner.err.log"
$collectorOutput = Join-Path $env:TEMP "hrp-soak-$runId.collector.out.log"
$collectorError = Join-Path $env:TEMP "hrp-soak-$runId.collector.err.log"
$queuePath = Join-Path $stateDirectory "local-collection-queue.db"
$manifestPath = Join-Path $env:TEMP "hrp-soak-current.json"

New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://127.0.0.1:5177"
$env:CollectionPlatform__StateDirectory = $stateDirectory
$env:ConnectionStrings__EventStore = "Data Source=$stateDirectory\eventstore.db"
$env:HORSE_RACING_API_KEY = "dev-api-key"
$env:CollectionQueue__Enabled = "true"
$env:CollectionQueue__Provider = "Local"
$env:CollectionQueue__LocalDatabasePath = $queuePath

$api = Start-Process -FilePath "dotnet" -ArgumentList @(
    "src/HorseRacingPrediction.Api/bin/Debug/net10.0/HorseRacingPrediction.Api.dll"
) -WorkingDirectory (Get-Location) -RedirectStandardOutput $apiOutput -RedirectStandardError $apiError `
    -WindowStyle Hidden -PassThru

$healthy = $false
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    Start-Sleep -Seconds 1
    try {
        if ((Invoke-WebRequest "http://localhost:5177/health" -SkipHttpErrorCheck).StatusCode -eq 200) {
            $healthy = $true
            break
        }
    } catch { }
}
if (!$healthy) { throw "Local API did not become healthy. See $apiError" }

$headers = @{ "X-Api-Key" = "dev-api-key" }
$body = @{ provider = "JRA"; year = 2026; month = 9; batchId = "soak:$runId" } | ConvertTo-Json
Invoke-RestMethod "http://localhost:5177/api/admin/collection/backfills" -Method Post -Headers $headers `
    -ContentType "application/json" -Body $body | Out-Null

$env:ApiClient__BaseUrl = "http://localhost:5177"
$env:ApiClient__ApiKey = "dev-api-key"
$env:LocalQueue__DatabasePath = $queuePath
$collector = Start-Process -FilePath "dotnet" -ArgumentList @(
    "src/HorseRacingPrediction.Collector/bin/Debug/net10.0/HorseRacingPrediction.Collector.dll",
    "--local-queue"
) -WorkingDirectory (Get-Location) -RedirectStandardOutput $collectorOutput `
    -RedirectStandardError $collectorError -WindowStyle Hidden -PassThru

$runner = Start-Process -FilePath "pwsh" -ArgumentList @(
    "-NoProfile", "-File", "tools/run-local-collection-soak.ps1",
    "-DurationMinutes", $DurationMinutes,
    "-MaxTasks", 80,
    "-StateDirectory", $stateDirectory,
    "-MetricsPath", $metricsPath,
    "-MonitorOnly"
) -WorkingDirectory (Get-Location) -RedirectStandardOutput $soakOutput -RedirectStandardError $soakError `
    -WindowStyle Hidden -PassThru

[ordered]@{
    runId = $runId
    startedAt = (Get-Date).ToString("o")
    expectedFinishAt = (Get-Date).AddMinutes($DurationMinutes).ToString("o")
    apiPid = $api.Id
    runnerPid = $runner.Id
    collectorPid = $collector.Id
    stateDirectory = $stateDirectory
    metricsPath = $metricsPath
    apiOutput = $apiOutput
    apiError = $apiError
    runnerOutput = $soakOutput
    runnerError = $soakError
    collectorOutput = $collectorOutput
    collectorError = $collectorError
    queuePath = $queuePath
} | ConvertTo-Json | Set-Content -LiteralPath $manifestPath

Get-Content $manifestPath
