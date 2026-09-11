param(
    [int]$DurationMinutes = 120,
    [string]$ApiBaseUrl = "http://localhost:5177",
    [string]$ApiKey = "dev-api-key",
    [int]$MaxTasks = 40,
    [string]$StateDirectory = "$env:TEMP\hrp-soak-20260911",
    [string]$MetricsPath = "$env:TEMP\hrp-collection-soak-metrics.jsonl",
    [int]$ApiPid = 0,
    [int]$CollectorPid = 0,
    [switch]$MonitorOnly
)

$ErrorActionPreference = "Stop"
$started = Get-Date
$deadline = $started.AddMinutes($DurationMinutes)
$headers = @{ "X-Api-Key" = $ApiKey }
$eventPath = Join-Path $env:TEMP "hrp-collection-soak-event.json"
$processed = [Collections.Generic.HashSet[string]]::new()
$latencies = [Collections.Generic.List[double]]::new()
$failures = 0

while ((Get-Date) -lt $deadline) {
    $cycleStarted = Get-Date
    try {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $endpointWatch = [Diagnostics.Stopwatch]::StartNew()
        $tasks = @(Invoke-RestMethod "$ApiBaseUrl/api/admin/collection/tasks?limit=500" -Headers $headers |
            ForEach-Object { $_ })
        $tasksLatencyMs = $endpointWatch.Elapsed.TotalMilliseconds
        $endpointWatch.Restart()
        $progress = Invoke-RestMethod "$ApiBaseUrl/api/admin/collection/progress" -Headers $headers
        $progressLatencyMs = $endpointWatch.Elapsed.TotalMilliseconds
        $endpointWatch.Restart()
        $backfills = @(Invoke-RestMethod "$ApiBaseUrl/api/admin/collection/backfills" -Headers $headers |
            ForEach-Object { $_ })
        $backfillsLatencyMs = $endpointWatch.Elapsed.TotalMilliseconds
        $watch.Stop()
        $latencies.Add($watch.Elapsed.TotalMilliseconds)

        $candidate = $tasks | Where-Object {
            $_.status -eq 1 -and !$processed.Contains([string]$_.taskId)
        } | Sort-Object @{Expression = "lane"; Ascending = $true},
            @{Expression = "priority"; Descending = $true}, availableAt | Select-Object -First 1

        if (!$MonitorOnly -and $null -ne $candidate -and $processed.Count -lt $MaxTasks) {
            $body = @{ taskId = $candidate.taskId; dispatchGeneration = 1 } | ConvertTo-Json -Compress
            @{ Records = @(@{ body = $body }) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $eventPath
            $env:COLLECTOR_EVENT_PATH = $eventPath
            $env:ApiClient__BaseUrl = $ApiBaseUrl
            $env:ApiClient__ApiKey = $ApiKey
            & dotnet run --project src/HorseRacingPrediction.Collector/HorseRacingPrediction.Collector.csproj --no-build -- --once
            if ($LASTEXITCODE -ne 0) { $failures++ }
            [void]$processed.Add([string]$candidate.taskId)
        }

        $sample = [ordered]@{
            at = (Get-Date).ToString("o")
            elapsedMinutes = [math]::Round(((Get-Date) - $started).TotalMinutes, 2)
            apiLatencyMs = [math]::Round($watch.Elapsed.TotalMilliseconds, 2)
            tasksLatencyMs = [math]::Round($tasksLatencyMs, 2)
            progressLatencyMs = [math]::Round($progressLatencyMs, 2)
            backfillsLatencyMs = [math]::Round($backfillsLatencyMs, 2)
            taskCount = $tasks.Count
            processed = $processed.Count
            ready = @($tasks | Where-Object status -eq 1).Count
            running = @($tasks | Where-Object status -eq 2).Count
            retryWaiting = @($tasks | Where-Object status -eq 3).Count
            waitingDiscovery = @($tasks | Where-Object status -eq 4).Count
            succeeded = @($tasks | Where-Object status -eq 5).Count
            failed = @($tasks | Where-Object status -in 6, 8).Count
            cancelled = @($tasks | Where-Object status -eq 7).Count
            backfillCount = $backfills.Count
            workerFailures = $failures
            apiWorkingSetMb = if ($ApiPid -gt 0) { [math]::Round((Get-Process -Id $ApiPid -ErrorAction SilentlyContinue).WorkingSet64 / 1MB, 2) } else { 0 }
            collectorWorkingSetMb = if ($CollectorPid -gt 0) { [math]::Round((Get-Process -Id $CollectorPid -ErrorAction SilentlyContinue).WorkingSet64 / 1MB, 2) } else { 0 }
            dbBytes = (Get-Item (Join-Path $StateDirectory "collection-platform.db") -ErrorAction SilentlyContinue).Length
        }
        $line = $sample | ConvertTo-Json -Compress
        Add-Content -LiteralPath $MetricsPath -Value $line
        Write-Output $line
    }
    catch {
        $failures++
        Write-Output (@{ at = (Get-Date).ToString("o"); error = $_.Exception.Message } | ConvertTo-Json -Compress)
    }

    $remaining = 60 - ((Get-Date) - $cycleStarted).TotalSeconds
    if ($remaining -gt 0) { Start-Sleep -Seconds ([math]::Ceiling($remaining)) }
}

$sorted = $latencies | Sort-Object
$p95Index = if ($sorted.Count -eq 0) { 0 } else { [math]::Min($sorted.Count - 1, [math]::Floor($sorted.Count * 0.95)) }
[ordered]@{
    startedAt = $started.ToString("o")
    finishedAt = (Get-Date).ToString("o")
    durationMinutes = [math]::Round(((Get-Date) - $started).TotalMinutes, 2)
    samples = $latencies.Count
    processedTasks = $processed.Count
    workerFailures = $failures
    apiLatencyAverageMs = if ($latencies.Count) { [math]::Round(($latencies | Measure-Object -Average).Average, 2) } else { 0 }
    apiLatencyP95Ms = if ($sorted.Count) { [math]::Round($sorted[$p95Index], 2) } else { 0 }
    metricsPath = $MetricsPath
} | ConvertTo-Json
