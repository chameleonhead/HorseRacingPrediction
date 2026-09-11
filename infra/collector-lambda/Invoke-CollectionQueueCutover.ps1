[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Provision', 'Activate', 'DeleteLegacy')]
    [string] $Stage,

    [Parameter(Mandatory)]
    [string] $VarFile,

    [string] $SmokeTaskId,

    [switch] $Apply
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Get-Command terraform -ErrorAction SilentlyContinue)) {
    throw 'terraform was not found on PATH.'
}

if (-not (Test-Path -LiteralPath $VarFile -PathType Leaf)) {
    throw "Terraform variable file was not found: $VarFile"
}

$stageSettings = switch ($Stage) {
    'Provision' { @('false', 'true') }
    'Activate' { @('true', 'true') }
    'DeleteLegacy' {
        if ([string]::IsNullOrWhiteSpace($SmokeTaskId)) {
            throw 'DeleteLegacy requires -SmokeTaskId from a successful smoke test.'
        }

        @('true', 'false')
    }
}

$activateReplacement = $stageSettings[0]
$retainLegacy = $stageSettings[1]
$planPath = Join-Path $PSScriptRoot ".terraform/collection-queue-$($Stage.ToLowerInvariant()).tfplan"
$terraformArgs = @(
    'plan',
    "-var-file=$VarFile",
    "-var=activate_resource_collection_queue=$activateReplacement",
    "-var=retain_legacy_collection_queues=$retainLegacy",
    "-out=$planPath"
)

Push-Location $PSScriptRoot
try {
    & terraform @terraformArgs
    if ($LASTEXITCODE -ne 0) {
        throw "terraform plan failed with exit code $LASTEXITCODE."
    }

    Write-Host "Review the saved $Stage plan before applying: $planPath"
    if ($Stage -eq 'DeleteLegacy') {
        Write-Host "Smoke evidence: task $SmokeTaskId"
        Write-Host 'The plan must delete only horse-racing-prediction-collector and horse-racing-prediction-collector-dlq from SQS.'
    }

    if ($Apply) {
        & terraform apply $planPath
        if ($LASTEXITCODE -ne 0) {
            throw "terraform apply failed with exit code $LASTEXITCODE."
        }
    }
}
finally {
    Pop-Location
}
