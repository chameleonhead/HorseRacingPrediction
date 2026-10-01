$ErrorActionPreference = 'Stop'

$scriptPath = Join-Path $PSScriptRoot '../../.codex/skills/production-incident-recovery/scripts/Get-CollectionFailureDiagnostics.ps1'
$content = Get-Content -LiteralPath $scriptPath -Raw

$required = @(
    '/api/v2/admin/collection/failure-notification-groups/${escapedGroupKey}?page=1&pageSize=100',
    '/api/v2/admin/collection/resources/$type/$provider/$resourceId/definitions/$definition',
    '/api/v2/admin/collection/execution-batches/$escapedBatchId',
    '$group = $groupEnvelope.page',
    'Detail = $resourceEnvelope.resource',
    ').batch'
)

foreach ($value in $required) {
    if (-not $content.Contains($value)) {
        throw "Collection diagnostics helper is missing the current API contract: $value"
    }
}

if ($content.Contains('/api/admin/collection/')) {
    throw 'Collection diagnostics helper must not use the removed pre-v2 administration routes.'
}

$parseErrors = $null
[System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path -LiteralPath $scriptPath), [ref] $null, [ref] $parseErrors) | Out-Null
if ($parseErrors.Count -ne 0) {
    throw ($parseErrors.Message -join [Environment]::NewLine)
}

Write-Output 'Collection failure diagnostics contract test passed.'
