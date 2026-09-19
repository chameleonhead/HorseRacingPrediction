[CmdletBinding()]
param(
    [switch]$ProvisionCredential,
    [switch]$Recover,
    [string]$DryRunReportPath
)

$ErrorActionPreference = 'Stop'
$stateRoot = Join-Path $env:LOCALAPPDATA 'HorseRacingPrediction\CollectionMonitor'
$credentialPath = Join-Path $stateRoot 'production-api-key.credential.xml'
$settingsPath = Join-Path $stateRoot 'settings.json'
$memoryPath = Join-Path $stateRoot 'memory.json'

if ($ProvisionCredential) {
    New-Item -ItemType Directory -Force -Path $stateRoot | Out-Null
    Read-Host 'Production API key' -AsSecureString | Export-Clixml -LiteralPath $credentialPath
    Write-Output "Encrypted credential saved for the current Windows user."
    exit 0
}

if ($DryRunReportPath) {
    $report = Get-Content -Raw -LiteralPath $DryRunReportPath | ConvertFrom-Json
}
else {
    if (-not (Test-Path -LiteralPath $settingsPath) -or -not (Test-Path -LiteralPath $credentialPath)) {
        throw 'Local monitor settings or encrypted credential are missing.'
    }
    $settings = Get-Content -Raw -LiteralPath $settingsPath | ConvertFrom-Json
    $secure = Import-Clixml -LiteralPath $credentialPath
    $apiKey = [System.Net.NetworkCredential]::new('', $secure).Password
    try {
        $base = $settings.baseUrl.TrimEnd('/') + '/api/admin/collection/monitoring'
        $report = Invoke-RestMethod -Uri "$base/findings" -Headers @{ 'X-Api-Key' = $apiKey } -TimeoutSec 90
        if ($Recover) {
            $preview = Invoke-RestMethod -Uri "$base/known-recovery/preview" -Headers @{ 'X-Api-Key' = $apiKey } -TimeoutSec 90
            if ($preview.safeToApply) {
                $null = Invoke-RestMethod -Method Post -Uri "$base/known-recovery/apply" `
                    -Headers @{ 'X-Api-Key' = $apiKey } -TimeoutSec 180
            }
        }
    }
    finally {
        $apiKey = $null
    }
}

$actionable = @($report.findings | Where-Object {
    $_.classification -in @('ProgramBug', 'UnknownHistoricalJobError', 'OperationalCondition', 0, 2, 3)
})
$unmapped = @($actionable | Where-Object {
    [string]::IsNullOrWhiteSpace($_.rootCauseHypothesis) -or
    [string]::IsNullOrWhiteSpace($_.ownerTask) -or
    [string]::IsNullOrWhiteSpace($_.nextSafeOperation)
})

New-Item -ItemType Directory -Force -Path $stateRoot | Out-Null
$memory = [ordered]@{
    observedAt = $report.completedAt
    outcome = $report.outcome
    findingCount = @($report.findings).Count
    actionableCount = $actionable.Count
    fingerprints = @($report.findings | ForEach-Object fingerprint)
}
$memory | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $memoryPath -Encoding utf8
$summary = [ordered]@{
    observedAt = $report.completedAt
    outcome = $report.outcome
    findingCount = @($report.findings).Count
    actionableCount = $actionable.Count
    requiresHuman = $unmapped.Count -gt 0
    actions = @($actionable | Select-Object fingerprint, kind, severity, rootCauseHypothesis, ownerTask, nextSafeOperation)
}
$summary | ConvertTo-Json -Depth 6
if ($unmapped.Count -gt 0) { exit 2 }
