param(
    [Parameter(Mandatory)][string]$DatabasePath,
    [string]$BaseUrl = 'http://127.0.0.1:5512'
)
$ErrorActionPreference = 'Stop'
$target = [Uri]$BaseUrl
if (-not $target.IsLoopback) { throw 'This synthetic repair verification is restricted to localhost.' }
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
$manifest = @{
    expectedVersion = $before.version; sourceUrl = 'https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01dde0106202604080520260926/AC';
    observedAt = [DateTimeOffset]::UtcNow.ToString('o'); gradeCode = 'G3'; sourceSnapshotSha256 = ('A' * 64);
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
$context = Invoke-RestMethod -Uri ($BaseUrl + "/api/races/$raceId/context") -Headers $headers
if ($context.gradeCode -ne 'G3' -or @($context.entries | Where-Object { $_.ownerName }).Count -ne 14) { throw 'Grade or owner enrichment failed.' }
[pscustomobject]@{ target = $BaseUrl; raceId = $raceId; entries = $context.entries.Count; owners = 14;
    grade = $context.gradeCode; previewReadOnly = $true; crossProcessLock = $true; repairEventsAdded = 1; idempotent = $true }
