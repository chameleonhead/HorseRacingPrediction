param([string]$BaseUri = 'https://100-49-86-109.sslip.io')
$ErrorActionPreference = 'Stop'
if ($BaseUri -ne 'https://100-49-86-109.sslip.io') { throw 'Unexpected preview host' }
$probe = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '../evidence/fixed-live-probe.json') | ConvertFrom-Json
$card = $probe | Where-Object { $_.parsed.entries }
if (@($card).Count -ne 1) { throw 'Exactly one verified card is required' }
$incidentCredential = Import-Clixml -LiteralPath (Join-Path $env:LOCALAPPDATA 'HorseRacingPrediction/CollectionMonitor/production-api-key.credential.xml')
$incidentHeaders = @{ 'X-Api-Key' = [System.Net.NetworkCredential]::new('', $incidentCredential).Password }
function Read-Resource([string]$type, [string]$id, [string]$definition) {
    try { Invoke-RestMethod -Method Get -Uri "$BaseUri/api/admin/collection/resources/$type/JRA/$([Uri]::EscapeDataString($id))/${definition}?requestHistoryPage=1&taskHistoryPage=1&attemptHistoryPage=1&historyPageSize=100" -Headers $incidentHeaders }
    catch { if ([int]$_.Exception.Response.StatusCode -eq 404) { return $null }; throw }
}
try {
    $raceId = 'race-65ab3995-5853-5270-aa59-ac6b9b83d15d'
    $before = Invoke-RestMethod -Method Get -Uri "$BaseUri/api/races/$raceId" -Headers $incidentHeaders
    $rows = foreach ($incoming in $card.parsed.entries) {
        $existing = @($before.entries | Where-Object horseId -eq $incoming.horseId)
        if ($existing.Count -ne 1) { throw "Non-unique stored Horse identity: $($incoming.horseId)" }
        $horse = Read-Resource 'Horse' $incoming.horseId 'horse-profile'
        $oldJob = Read-Resource 'Jockey' $existing[0].jockeyId 'jockey-profile'
        $newJob = Read-Resource 'Jockey' $incoming.jockeyId 'jockey-profile'
        $matchingEvidence = @($horse.tasks | Where-Object {
            $_.metadata.sourceIdentity -eq $incoming.HorseSourceIdentity -and
            $_.metadata.sourceUrl -eq $incoming.HorseSourceIdentity -and
            $_.metadata.requestedByRaceId -eq $raceId -and
            $_.metadata.discoveredFromId -eq $raceId -and
            $_.metadata.discoveredFromType -eq 'Race' -and $_.metadata.discoveredFromProvider -eq 'JRA'
        })
        $verified = $matchingEvidence.Count -gt 0
        [ordered]@{
            entryId = $existing[0].entryId; horseId = $incoming.horseId; horseName = $incoming.HorseName
            officialSourceIdentity = $incoming.HorseSourceIdentity; sourceVerified = $verified
            oldJockeyId = $existing[0].jockeyId; oldJockeyName = $existing[0].jockeyName
            newJockeyId = $incoming.jockeyId; newJockeyName = $incoming.JockeyName
            changes = $existing[0].jockeyId -ne $incoming.jockeyId
            oldTasks = @($oldJob.tasks | Select-Object taskId,status,requestedRevision)
            newTasks = @($newJob.tasks | Select-Object taskId,status,requestedRevision)
            oldRequests = @($oldJob.requests | Select-Object requestId,requestedRevision,reason)
            newRequests = @($newJob.requests | Select-Object requestId,requestedRevision,reason)
            horseEvidenceTaskIds = @($matchingEvidence.taskId)
            exclusion = $(if (-not $verified) { 'Stored source identity could not be corroborated' } else { $null })
        }
    }
    $after = Invoke-RestMethod -Method Get -Uri "$BaseUri/api/races/$raceId" -Headers $incidentHeaders
    $unchanged = ($before | ConvertTo-Json -Depth 30 -Compress) -eq ($after | ConvertTo-Json -Depth 30 -Compress)
    if (-not $unchanged) { throw 'Race changed during preview; rerun before approval' }
    [ordered]@{ capturedAt = [DateTimeOffset]::UtcNow; mode = 'GET only'; raceId = $raceId
        raceResource = Read-Resource 'Race' '20260927:Nakayama:11' 'race-detail'
        raceUnchanged = $unchanged; globalScan = $false; rows = @($rows)
    } | ConvertTo-Json -Depth 40
} finally { Remove-Variable incidentCredential,incidentHeaders }
