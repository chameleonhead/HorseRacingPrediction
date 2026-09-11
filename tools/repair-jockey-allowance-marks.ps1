[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string] $ApiKey,

    [string] $BaseUrl = 'http://localhost:5177'
)

$ErrorActionPreference = 'Stop'
$headers = @{ 'X-Api-Key' = $ApiKey }
$allowanceMarkPattern = '^[▲△☆★◇▽]+\s*'
$jockeys = @()

for ($page = 1; ; $page++) {
    $response = Invoke-RestMethod `
        -Uri "$($BaseUrl.TrimEnd('/'))/api/jockeys?page=$page&pageSize=100" `
        -Headers $headers
    $jockeys += @($response.items)
    if ($jockeys.Count -ge $response.totalCount) {
        break
    }
}

$candidates = @($jockeys | Where-Object {
    $_.displayName -match $allowanceMarkPattern -or
    $_.normalizedName -match $allowanceMarkPattern
})
$normalizedLookup = $jockeys | Group-Object {
    $_.normalizedName -replace $allowanceMarkPattern, ''
} -AsHashTable -AsString

$corrected = 0
$duplicates = 0
foreach ($jockey in $candidates) {
    $cleanedDisplayName = $jockey.displayName -replace $allowanceMarkPattern, ''
    $cleanedNormalizedName = $jockey.normalizedName -replace $allowanceMarkPattern, ''
    $sameName = @($normalizedLookup[$cleanedNormalizedName] | Where-Object {
        $_.jockeyId -ne $jockey.jockeyId
    })

    if ($sameName.Count -gt 0) {
        $duplicates++
        Write-Warning "重複候補のため補正をスキップしました: $($jockey.jockeyId) -> $cleanedNormalizedName"
        continue
    }

    if ($PSCmdlet.ShouldProcess($jockey.jockeyId, "騎手名を '$cleanedDisplayName' に補正")) {
        $body = @{
            displayName = $cleanedDisplayName
            normalizedName = $cleanedNormalizedName
            affiliationCode = $null
            reason = '騎手名に混入したJRA減量記号を除去'
        } | ConvertTo-Json

        Invoke-RestMethod `
            -Method Patch `
            -Uri "$($BaseUrl.TrimEnd('/'))/api/jockeys/$([uri]::EscapeDataString($jockey.jockeyId))" `
            -Headers $headers `
            -ContentType 'application/json' `
            -Body $body | Out-Null
        $corrected++
    }
}

[pscustomobject]@{
    Scanned = $jockeys.Count
    Candidates = $candidates.Count
    Corrected = $corrected
    DuplicateCandidates = $duplicates
}
