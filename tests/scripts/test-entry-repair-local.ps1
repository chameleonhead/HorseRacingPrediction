param(
    [Parameter(Mandatory)][string]$DatabasePath,
    [string]$BaseUrl = 'http://127.0.0.1:5512',
    [string]$AlternateBaseUrl
)
$ErrorActionPreference = 'Stop'
$target = [Uri]$BaseUrl
if (-not $target.IsLoopback) { throw 'This synthetic repair verification is restricted to localhost.' }
if (-not $AlternateBaseUrl) { $AlternateBaseUrl = $BaseUrl }
if (-not ([Uri]$AlternateBaseUrl).IsLoopback) { throw 'The second API must also be localhost.' }
$resolvedDatabase = (Resolve-Path -LiteralPath $DatabasePath).Path
$headers = @{ 'X-Api-Key' = 'local-repair-verification-only' }
function Post-LocalJson([string]$path, $body) {
    Invoke-RestMethod -Method Post -Uri ($BaseUrl + $path) -Headers $headers -ContentType 'application/json' -Body ($body | ConvertTo-Json -Depth 20)
}
$entries = @(1..14 | ForEach-Object {
    @{ horseNumber = $_; horseName = "ローカル検証馬$_"; jockeyName = 'ローカル検証騎手'; bodyWeight = 450 + $_;
       horseSourceIdentity = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud00202410$($_.ToString('D4'))/AB" }
})
$created = Post-LocalJson '/api/races/result-bulk' @{
    raceDate = '2026-09-26'; racecourseCode = '中山'; raceNumber = 5; raceName = 'ローカル14頭補正検証';
    gradeCode = 'G2'; entryCount = 14; isRaceCard = $true; entries = $entries
}
if (-not $created.corePersisted -or $created.errors.Count -ne 0) { throw ($created | ConvertTo-Json -Depth 8) }
$raceId = $created.raceId
$repairPath = "/api/admin/races/$raceId/entry-repair"
$before = Invoke-RestMethod -Uri ($BaseUrl + $repairPath) -Headers $headers
$holdRequest = @{ operationId = [Guid]::NewGuid().ToString(); expectedGeneration = 0; reason = 'isolated cross-process smoke' }
$hold = Invoke-RestMethod -Method Post -Uri ($AlternateBaseUrl + $repairPath + '/hold') -Headers $headers -ContentType 'application/json' -Body ($holdRequest | ConvertTo-Json)
if (-not $hold.isQuiescent) { throw ($hold | ConvertTo-Json -Depth 10) }
$request = @{ resourceType = 0; provider = 'JRA'; resourceId = '20260926:Nakayama:5'; definitionId = 'race-detail'; requestedRevision = 4; reason = 5 }
$deferred = Post-LocalJson '/api/admin/collection/requests' $request
if (-not $deferred.deferredByRepairHold -or $deferred.createdTask) { throw 'Cross-process hold did not defer the request.' }
$heldRead = Invoke-WebRequest -Uri ($BaseUrl + "/api/races/$raceId/context") -Headers $headers -SkipHttpErrorCheck
if ($heldRead.StatusCode -ne 409) { throw 'Held prediction context was exposed.' }
$manifest = @{
    expectedVersion = $before.version; sourceUrl = 'https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01dde0106202604080520260926/AC';
    observedAt = [DateTimeOffset]::UtcNow.ToString('o'); gradeCode = 'G3'; sourceSnapshotSha256 = ('A' * 64);
    holdOperationId = $hold.operationId; holdGeneration = $hold.generation;
    horses = @($entries | ForEach-Object { @{ sourceUrl = $_.horseSourceIdentity; horseNumber = $_.horseNumber % 14 + 1;
        gateNumber = [int][Math]::Floor(($_.horseNumber % 14) / 2) + 1; ownerName = "所有者$($_.horseNumber)" } })
}
$preview = Post-LocalJson ($repairPath + '/preview') $manifest
if (-not $preview.eligible) { throw ($preview | ConvertTo-Json -Depth 10) }
$afterPreview = Invoke-RestMethod -Uri ($BaseUrl + $repairPath) -Headers $headers
if ($afterPreview.version -ne $before.version) { throw 'Preview changed event version.' }
$apply = @{ operationId = [Guid]::NewGuid().ToString(); fingerprint = $preview.fingerprint; manifest = $manifest }
$lockName = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($raceId))) + '.lock'
$lockPath = Join-Path ($resolvedDatabase + '.race-locks') $lockName
$stream = [System.IO.FileStream]::new($lockPath, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(25)
$client.DefaultRequestHeaders.Add('X-Api-Key', $headers['X-Api-Key'])
try {
    $content = [System.Net.Http.StringContent]::new(($apply | ConvertTo-Json -Depth 20), [Text.Encoding]::UTF8, 'application/json')
    $pending = $client.PostAsync($BaseUrl + $repairPath + '/apply', $content)
    Start-Sleep -Milliseconds 400
    if ($pending.IsCompleted) { throw 'API did not respect the lock held by another process.' }
    $stream.Dispose()
    $response = $pending.GetAwaiter().GetResult()
    if (-not $response.IsSuccessStatusCode) { throw $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() }
} finally { $stream.Dispose(); $client.Dispose() }
$repeated = Post-LocalJson ($repairPath + '/apply') $apply
$after = Invoke-RestMethod -Uri ($BaseUrl + $repairPath) -Headers $headers
if (-not $repeated.verified -or $after.version -ne $before.version + 1 -or $after.blockers.Count -ne 0) { throw 'Repair/retry verification failed.' }
$package = Join-Path ($resolvedDatabase + '.race-locks/backups') $apply.operationId
foreach ($file in @('events.db', 'collection.db', 'manifest.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $package $file))) { throw "Backup package lacks $file" }
}
$release = @{ operationId = [Guid]::NewGuid().ToString(); holdOperationId = $hold.operationId; holdGeneration = $hold.generation;
    expectedVersion = $after.version; assignmentFingerprint = $repeated.assignmentFingerprint; repairOperationId = $apply.operationId; repairFingerprint = $apply.fingerprint }
$released = Post-LocalJson ($repairPath + '/release') $release
$releaseAgain = Post-LocalJson ($repairPath + '/release') $release
if ($released.isActive -or $releaseAgain.isActive) { throw 'Verified repair did not release idempotently.' }
$receipt = Post-LocalJson '/api/admin/collection/requests' $request
if (-not $receipt.taskId -or $receipt.createdTask) { throw 'Release did not materialize exactly one current request.' }
$acquired = Post-LocalJson "/api/internal/collection/tasks/$($receipt.taskId)/acquire" @{ dispatchGeneration = 1; leaseSeconds = 60 }
if (-not $acquired.task -or $acquired.task.raceHoldGeneration -ne $hold.generation -or $acquired.task.entryAssignmentFingerprint -ne $repeated.assignmentFingerprint) {
    throw 'Worker acquire did not carry the validated assignment fence.'
}
$odds = @{ observedAt = [DateTimeOffset]::UtcNow.ToString('o'); entries = @(@{ horseNumber = 1; winOdds = 2.5; popularity = 1 }) }
$stale = Invoke-WebRequest -Method Post -Uri ($AlternateBaseUrl + "/api/admin/races/$raceId/odds-snapshots") -Headers $headers -ContentType 'application/json' -Body ($odds | ConvertTo-Json -Depth 5) -SkipHttpErrorCheck
if ($stale.StatusCode -ne 409 -or $stale.Content -notmatch 'StaleRaceAssignmentFence') { throw 'A delayed unversioned odds write was accepted.' }
$fencedHeaders = $headers.Clone()
$fencedHeaders['X-Collection-Task-Id'] = $receipt.taskId
$fencedHeaders['X-Collection-Lease-Token'] = $acquired.task.leaseToken
$fencedHeaders['X-Race-Hold-Generation'] = [string]$acquired.task.raceHoldGeneration
$fencedHeaders['X-Race-Assignment-Fingerprint'] = $acquired.task.entryAssignmentFingerprint
Invoke-RestMethod -Method Post -Uri ($AlternateBaseUrl + "/api/admin/races/$raceId/odds-snapshots") -Headers $fencedHeaders -ContentType 'application/json' -Body ($odds | ConvertTo-Json -Depth 5) | Out-Null
Post-LocalJson "/api/internal/collection/tasks/$($receipt.taskId)/complete" @{ leaseToken = $acquired.task.leaseToken; result = 1 } | Out-Null
$context = Invoke-RestMethod -Uri ($BaseUrl + "/api/races/$raceId/context") -Headers $headers
if ($context.gradeCode -ne 'G3' -or @($context.entries | Where-Object { $_.ownerName }).Count -ne 14) { throw 'Grade or owner enrichment failed.' }
[pscustomobject]@{ target = $BaseUrl; raceId = $raceId; entries = $context.entries.Count; owners = 14;
    grade = $context.gradeCode; previewReadOnly = $true; crossProcessLock = $true; repairEventsAdded = 1; idempotent = $true;
    durableHold = $true; backupVerified = $true; delayedOddsRejected = $true; currentWorkerWrite = $true; isolatedDirectory = [IO.Path]::GetDirectoryName($resolvedDatabase) }
