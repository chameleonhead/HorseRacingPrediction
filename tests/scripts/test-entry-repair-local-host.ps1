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
try {
    foreach ($key in $settings.Keys) {
        $oldSettings[$key] = [Environment]::GetEnvironmentVariable($key)
        [Environment]::SetEnvironmentVariable($key, $settings[$key])
    }
    $start = @{ FilePath = 'dotnet'; ArgumentList = @($dll, '--urls', $baseUrl, '--contentRoot', $apiRoot);
        PassThru = $true; RedirectStandardOutput = Join-Path $directory 'api.log'; RedirectStandardError = Join-Path $directory 'api-error.log' }
    if ($IsWindows) { $start.WindowStyle = 'Hidden' }
    $process = Start-Process @start
    $ready = $false
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        if ($process.HasExited) {
            Get-Content -LiteralPath (Join-Path $directory 'api-error.log') -Tail 50
            throw "Local API exited; inspect $directory"
        }
        try { $ready = (Invoke-RestMethod "$baseUrl/health" -TimeoutSec 1).status -eq 'ok' } catch { }
        if ($ready) { break }
        Start-Sleep -Milliseconds 200
    }
    if (-not $ready) { throw "Local API did not become ready; inspect $directory" }
    $secondListener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $secondListener.Start()
    $secondUrl = "http://127.0.0.1:$($secondListener.LocalEndpoint.Port)"
    $secondListener.Stop()
    $secondStart = @{ FilePath = 'dotnet'; ArgumentList = @($dll, '--urls', $secondUrl, '--contentRoot', $apiRoot);
        PassThru = $true; RedirectStandardOutput = Join-Path $directory 'api-second.log'; RedirectStandardError = Join-Path $directory 'api-second-error.log' }
    if ($IsWindows) { $secondStart.WindowStyle = 'Hidden' }
    $secondProcess = Start-Process @secondStart
    $secondReady = $false
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        if ($secondProcess.HasExited) { throw "Second API exited; inspect $directory" }
        try { $secondReady = (Invoke-RestMethod "$secondUrl/health" -TimeoutSec 1).status -eq 'ok' } catch { }
        if ($secondReady) { break }
        Start-Sleep -Milliseconds 200
    }
    if (-not $secondReady) { throw "Second API did not become ready; inspect $directory" }
    & (Join-Path $PSScriptRoot 'test-entry-repair-local.ps1') -DatabasePath (Join-Path $directory 'eventstore.db') -BaseUrl $baseUrl -AlternateBaseUrl $secondUrl
} finally {
    if ($secondProcess -and -not $secondProcess.HasExited) { Stop-Process -Id $secondProcess.Id }
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id }
    foreach ($key in $oldSettings.Keys) { [Environment]::SetEnvironmentVariable($key, $oldSettings[$key]) }
}
