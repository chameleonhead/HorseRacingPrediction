param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$apiRoot = Join-Path $repository 'src/HorseRacingPrediction.Api'
$dll = Join-Path $apiRoot "bin/$Configuration/net10.0/HorseRacingPrediction.Api.dll"
if (-not (Test-Path -LiteralPath $dll)) { throw 'Build the API before running the isolated host smoke test.' }
$directory = Join-Path ([IO.Path]::GetTempPath()) ('hrp-repair-host-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory | Out-Null
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$baseUrl = "http://127.0.0.1:$port"
$settings = @{
    'ConnectionStrings__EventStore' = 'Data Source=' + (Join-Path $directory 'eventstore.db')
    'CollectionPlatform__StateDirectory' = Join-Path $directory 'collection'
    'PredictionScheduling__StateDirectory' = Join-Path $directory 'prediction'
    'DataProtection__KeysDirectory' = Join-Path $directory 'keys'
    'ApiKey__Key' = 'local-repair-verification-only'
    'ASPNETCORE_ENVIRONMENT' = 'Production'
    'CollectionQueue__Enabled' = 'false'
    # The production host constructs its SNS client even when there are no alerts.
    # Never inherit a developer's AWS credentials or depend on their saved region.
    'AWS_REGION' = 'ap-northeast-1'
    'AWS_DEFAULT_REGION' = 'ap-northeast-1'
    'AWS_ACCESS_KEY_ID' = 'local-smoke-only'
    'AWS_SECRET_ACCESS_KEY' = 'local-smoke-only'
    'AWS_SESSION_TOKEN' = ''
    'AWS_EC2_METADATA_DISABLED' = 'true'
    'JobFailureNotifications__TopicArn' = ''
}
$oldSettings = @{}
$process = $null
$secondProcess = $null

function Get-LoopbackUrl {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return "http://127.0.0.1:$($listener.LocalEndpoint.Port)" }
    finally { $listener.Stop() }
}

function Start-LocalApi([string]$url, [string]$logPrefix) {
    $start = @{ FilePath = 'dotnet'; ArgumentList = @($dll, '--urls', $url, '--contentRoot', $apiRoot);
        PassThru = $true; RedirectStandardOutput = Join-Path $directory "$logPrefix.log";
        RedirectStandardError = Join-Path $directory "$logPrefix-error.log" }
    if ($IsWindows) { $start.WindowStyle = 'Hidden' }
    return Start-Process @start
}

function Wait-ForLocalApi($apiProcess, [string]$url, [string]$logPrefix) {
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        if ($apiProcess.HasExited) {
            Get-Content -LiteralPath (Join-Path $directory "$logPrefix-error.log") -Tail 50
            throw "Local API exited; inspect $directory"
        }
        try { if ((Invoke-RestMethod "$url/health" -TimeoutSec 1).status -eq 'ok') { return } } catch { }
        Start-Sleep -Milliseconds 200
    }
    throw "Local API did not become ready; inspect $directory"
}

function Stop-LocalApi($apiProcess) {
    if (-not $apiProcess -or $apiProcess.HasExited) { return }
    Stop-Process -Id $apiProcess.Id
    if (-not $apiProcess.WaitForExit(10000)) {
        $apiProcess.Kill($true)
        if (-not $apiProcess.WaitForExit(5000)) { throw "Local API process $($apiProcess.Id) did not exit." }
    }
    $apiProcess.Refresh()
    if (-not $apiProcess.HasExited) { throw "Local API process $($apiProcess.Id) remains alive." }
}

try {
    foreach ($key in $settings.Keys) {
        $oldSettings[$key] = [Environment]::GetEnvironmentVariable($key)
        [Environment]::SetEnvironmentVariable($key, $settings[$key])
    }
    $baseUrl = Get-LoopbackUrl
    $process = Start-LocalApi $baseUrl 'api'
    Wait-ForLocalApi $process $baseUrl 'api'
    # A second normal API over the same configured source set must remain status-only.
    $overlapUrl = Get-LoopbackUrl
    $secondProcess = Start-LocalApi $overlapUrl 'api-overlap'
    Wait-ForLocalApi $secondProcess $overlapUrl 'api-overlap'
    $headers = @{ 'X-Api-Key' = 'local-repair-verification-only' }
    $probeRaceId = [Guid]::NewGuid().ToString()
    $overlap = Invoke-WebRequest -Uri "$overlapUrl/api/v2/admin/races/$probeRaceId/entry-repair/hold-state" `
        -Headers $headers -SkipHttpErrorCheck -TimeoutSec 5
    $overlapBody = $overlap.Content
    if ($overlapBody -is [byte[]]) { $overlapBody = [Text.Encoding]::UTF8.GetString($overlapBody) }
    $overlapProblem = $overlapBody | ConvertFrom-Json
    if ($overlap.StatusCode -ne 503 -or $overlapProblem.title -ne 'Application startup is blocked by migration state.') {
        throw "Concurrent API ownership did not fail closed as expected; status=$($overlap.StatusCode), body=$overlapBody"
    }
    Stop-LocalApi $secondProcess
    $secondProcess = $null

    $helper = Join-Path $PSScriptRoot 'test-entry-repair-local.ps1'
    $databasePath = Join-Path $directory 'eventstore.db'
    $prepared = & $helper -DatabasePath $databasePath -BaseUrl $baseUrl -AlternateBaseUrl $baseUrl -Phase PrepareHold
    if (-not $prepared.prepared -or -not $prepared.raceId -or -not $prepared.holdOperationId) {
        throw 'First API process did not return the prepared race and durable hold receipt.'
    }

    # Hold state must survive a complete owner release and a new normal API owner.
    Stop-LocalApi $process
    $process = $null
    $replacementUrl = Get-LoopbackUrl
    $process = Start-LocalApi $replacementUrl 'api-replacement'
    Wait-ForLocalApi $process $replacementUrl 'api-replacement'
    $result = & $helper -DatabasePath $databasePath -BaseUrl $replacementUrl -AlternateBaseUrl $replacementUrl `
        -Phase ContinueAfterRestart -RaceId $prepared.raceId -ExpectedHoldOperationId $prepared.holdOperationId `
        -ExpectedHoldGeneration $prepared.holdGeneration -ExpectedVersion $prepared.inspectionVersion
    [pscustomobject]@{
        result = $result; overlappingOwnerRejected = $true; holdPersistedAcrossProcessRestart = $true
        normalApiOwnersWereSequential = $true; isolatedDirectory = $directory
    }
} finally {
    Stop-LocalApi $secondProcess
    Stop-LocalApi $process
    foreach ($key in $oldSettings.Keys) { [Environment]::SetEnvironmentVariable($key, $oldSettings[$key]) }
}
