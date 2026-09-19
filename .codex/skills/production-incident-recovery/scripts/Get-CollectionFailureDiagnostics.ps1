[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [uri] $BaseUri,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $FailureGroupKey,

    [ValidateRange(1, 100)]
    [int] $HistoryPageSize = 100,

    [Security.SecureString] $ApiKey
)

Set-StrictMode -Version Latest

if ($BaseUri.Scheme -ne 'https' -and -not $BaseUri.IsLoopback) {
    throw 'HTTPS is required except for loopback local development.'
}

if (-not $ApiKey) {
    $ApiKey = Read-Host 'Collection administration API key' -AsSecureString
}

$secretPointer = [IntPtr]::Zero
$plainApiKey = $null

function ConvertTo-EscapedPathSegment {
    param([Parameter(Mandatory)][string] $Value)
    return [uri]::EscapeDataString($Value)
}

function Invoke-CollectionDiagnosticGet {
    param([Parameter(Mandatory)][string] $RelativePath)

    $requestUri = [uri]::new($BaseUri, $RelativePath)
    Invoke-RestMethod -Method Get -Uri $requestUri -Headers @{ 'X-Api-Key' = $plainApiKey }
}

try {
    $secretPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($ApiKey)
    $plainApiKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($secretPointer)

    $escapedGroupKey = ConvertTo-EscapedPathSegment $FailureGroupKey
    $groupPath = "/api/admin/collection/failure-notifications/groups/$escapedGroupKey?page=1&pageSize=100"
    $group = Invoke-CollectionDiagnosticGet $groupPath

    $resources = foreach ($item in $group.items) {
        $type = ConvertTo-EscapedPathSegment ([string] $item.resource.type)
        $provider = ConvertTo-EscapedPathSegment ([string] $item.resource.provider)
        $resourceId = ConvertTo-EscapedPathSegment ([string] $item.resource.id)
        $definition = ConvertTo-EscapedPathSegment ([string] $item.definition.value)
        $resourcePath = "/api/admin/collection/resources/$type/$provider/$resourceId/$definition" +
            "?requestHistoryPage=1&taskHistoryPage=1&attemptHistoryPage=1&historyPageSize=$HistoryPageSize"

        [pscustomobject]@{
            Resource = $item.resource
            Definition = $item.definition
            Detail = Invoke-CollectionDiagnosticGet $resourcePath
        }
    }

    $executionBatchIds = @(
        $resources.Detail.attempts.executionBatchId |
            Where-Object { $_ } |
            Sort-Object -Unique
    )
    $executionBatches = foreach ($executionBatchId in $executionBatchIds) {
        $escapedBatchId = ConvertTo-EscapedPathSegment ([string] $executionBatchId)
        Invoke-CollectionDiagnosticGet "/api/admin/collection/execution-batches/$escapedBatchId"
    }

    [pscustomobject]@{
        ObservedAt = [DateTimeOffset]::Now
        BaseUri = $BaseUri.AbsoluteUri
        FailureGroup = $group
        Resources = @($resources)
        ExecutionBatches = @($executionBatches)
        MutationPerformed = $false
    }
}
finally {
    $plainApiKey = $null
    if ($secretPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($secretPointer)
    }
}
