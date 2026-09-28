param(
    [string]$SourceRuntime = "$env:LOCALAPPDATA\HorseRacingPrediction\soak-20260928\isolated-run-20260928-065157",
    [string]$ResumeRuntime = '',
    [switch]$QuiesceOnly,
    [switch]$RetryNakayama2,
    [switch]$ResubmitMissingTargets,
    [switch]$RecollectPayoutAnomalies,
    [switch]$RetryPayoutAnomalies,
    [int]$MaxResubmitMissingTargets = 0,
    [int]$LocalPort = 5194,
    [string]$ApiProject = "C:\Users\yuto.nagano\source\repos\HorseRacingPrediction\src\HorseRacingPrediction.Api\HorseRacingPrediction.Api.csproj",
    [string]$CollectorProject = "C:\Users\yuto.nagano\source\repos\HorseRacingPrediction\src\HorseRacingPrediction.Collector\HorseRacingPrediction.Collector.csproj"
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$null = Add-Type -AssemblyName System.Net.Http
$repo = 'C:\Users\yuto.nagano\source\repos\HorseRacingPrediction'
$stamp = [DateTimeOffset]::Now.ToString('yyyyMMdd-HHmmss')
$runtime = if ($ResumeRuntime) { (Resolve-Path -LiteralPath $ResumeRuntime).Path } else { Join-Path "$env:LOCALAPPDATA\HorseRacingPrediction\weekend-results-20260928" "run-$stamp" }
$baseUrl = if ($QuiesceOnly) { 'http://127.0.0.1:5195' } else { "http://127.0.0.1:$LocalPort" }
$apiProcess = $null
$collectorProcess = $null
$client = $null
$phase = 'baseline-copy'
$statusMap = @{ '0'='Pending'; '1'='Ready'; '2'='Running'; '3'='RetryWaiting'; '4'='WaitingDiscovery'; '5'='Succeeded'; '6'='Failed'; '7'='Cancelled'; '8'='DeadLetter' }
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
$keyBytes = New-Object byte[] 32
$rng.GetBytes($keyBytes)
$key = [Convert]::ToBase64String($keyBytes)
$rng.Dispose()

function Invoke-Api([string]$Method, [string]$Path, $Body = $null) {
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), ($baseUrl + $Path))
    [void]$request.Headers.TryAddWithoutValidation('X-Api-Key', $script:key)
    if ($null -ne $Body) {
        $json = ConvertTo-Json -InputObject $Body -Depth 10 -Compress
        $request.Content = [System.Net.Http.StringContent]::new($json, [Text.Encoding]::UTF8, 'application/json')
    }
    $response = $script:client.SendAsync($request).GetAwaiter().GetResult()
    $status = [int]$response.StatusCode
    $content = if ($response.Content) { $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() } else { '' }
    $response.Dispose(); $request.Dispose()
    if ($status -lt 200 -or $status -ge 300) {
        $script:apiErrorCategory = 'unclassified'
        try {
            $safeBody = $content | ConvertFrom-Json
            $safeMessage = [string](Get-Property $safeBody 'message')
            $script:apiValidationKeys = @($safeBody.PSObject.Properties.Name | Where-Object { $_ -match '^[A-Za-z][A-Za-z0-9_.]*$' })
            $safeErrors = Get-Property $safeBody 'errors'
            if ($safeErrors) { $script:apiValidationKeys += @($safeErrors.PSObject.Properties.Name | Where-Object { $_ -match '^[A-Za-z][A-Za-z0-9_.]*$' }) }
            $script:apiErrorCategory = switch ($safeMessage) {
                'Resource mode accepts only resource.' { 'resource_mode_shape' }
                'ExplicitUrl must be an absolute HTTP(S) URL.' { 'explicit_url_validation' }
                'mode must be Resource or SourceUrl.' { 'mode_validation' }
                default { 'unclassified' }
            }
        } catch { }
        throw "api_status_$status"
    }
    if ([string]::IsNullOrWhiteSpace($content)) { return $null }
    return $content | ConvertFrom-Json
}

function Get-Tasks([int]$Page, [bool]$ActiveOnly = $false) {
    $suffix = if ($ActiveOnly) { '&statuses=Ready,Running,RetryWaiting' } else { '' }
    Invoke-Api GET "/api/v2/admin/collection/tasks?page=$Page&pageSize=100$suffix"
}

function Get-Items($Value) {
    if ($null -eq $Value) { return @() }
    try {
        $script:itemsShape = @($Value.PSObject.Properties | ForEach-Object { $_.Name + ':' + $_.Value.GetType().Name })
        foreach ($name in @('items','Items','tasks','Tasks')) {
            $property = $null
            foreach ($candidate in $Value.PSObject.Properties) {
                if ($candidate.Name -ieq $name) { $property = $candidate; break }
            }
            if ($null -ne $property -and $null -ne $property.Value) {
                $items = @($property.Value)
                return $items
            }
        }
    } catch {
        $script:itemsFailureType = $_.Exception.GetType().Name
        throw 'items_extract_failure'
    }
    return @()
}

function Get-Property($Value, [string]$Name) {
    if ($null -eq $Value) { return $null }
    foreach ($property in $Value.PSObject.Properties) { if ($property.Name -ieq $Name) { return $property.Value } }
    return $null
}

try {
    $root = (Resolve-Path -LiteralPath $SourceRuntime).Path
    if (!(Test-Path -LiteralPath (Join-Path $root 'eventstore.db')) -or
        !(Test-Path -LiteralPath (Join-Path $root 'platform\collection-platform.db')) -or
        !(Test-Path -LiteralPath (Join-Path $root 'prediction-scheduling\prediction-executions.db'))) { throw 'baseline_missing' }
    if (!$ResumeRuntime) {
        if (Test-Path -LiteralPath $runtime) { throw 'runtime_exists' }
        $phase = 'create-runtime-directory'
        New-Item -ItemType Directory -Path $runtime -Force | Out-Null
        $phase = 'copy-baseline-databases'
        foreach ($relative in @('eventstore.db','platform\collection-platform.db','platform\local-collection-queue.db','prediction-scheduling\prediction-executions.db')) {
            $source = Join-Path $root $relative
            if (!(Test-Path -LiteralPath $source)) { if ($relative -like '*local-collection-queue*') { continue }; throw 'baseline_file_missing' }
            $target = Join-Path $runtime $relative
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            Copy-Item -LiteralPath $source -Destination $target
            foreach ($sidecar in @('-wal','-shm')) {
                if (Test-Path -LiteralPath ($source + $sidecar)) { Copy-Item -LiteralPath ($source + $sidecar) -Destination ($target + $sidecar) }
            }
        }
    }
    $phase = 'create-runtime-logs'
    New-Item -ItemType Directory -Path (Join-Path $runtime 'logs') -Force | Out-Null
    $phase = 'prepare-isolated-api'
    $client = [System.Net.Http.HttpClient]::new(); $client.Timeout = [TimeSpan]::FromSeconds(30)
    $env:ASPNETCORE_URLS = $baseUrl
    $env:ApiKey__Key = $key
    $env:ConnectionStrings__EventStore = "Data Source=$(Join-Path $runtime 'eventstore.db')"
    $env:CollectionPlatform__StateDirectory = Join-Path $runtime 'platform'
    $env:CollectionOrchestration__BackgroundSchedulersEnabled = 'false'
    $env:CollectionQueue__Enabled = 'true'; $env:CollectionQueue__Provider = 'Local'
    $env:CollectionQueue__LocalDatabasePath = Join-Path $runtime 'platform\local-collection-queue.db'
    $env:PredictionScheduling__StateDirectory = Join-Path $runtime 'prediction-scheduling'
    $env:DataProtection__KeysDirectory = Join-Path $runtime 'data-protection'
    $null = New-Item -ItemType Directory -Path $env:DataProtection__KeysDirectory -Force
    $phase = 'start-isolated-api'
    $apiAssembly = Join-Path $repo 'src\HorseRacingPrediction.Api\bin\Release\net10.0\HorseRacingPrediction.Api.dll'
    $apiProcess = Start-Process dotnet -ArgumentList @($apiAssembly) -WorkingDirectory $repo -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runtime 'logs\api.stdout.log') -RedirectStandardError (Join-Path $runtime 'logs\api.stderr.log')
    foreach ($name in @('ApiKey__Key','ConnectionStrings__EventStore','CollectionPlatform__StateDirectory','CollectionOrchestration__BackgroundSchedulersEnabled','CollectionQueue__Enabled','CollectionQueue__Provider','CollectionQueue__LocalDatabasePath','PredictionScheduling__StateDirectory','DataProtection__KeysDirectory')) { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
    $phase = 'await-isolated-api-health'
    $ready = $false
    for ($i = 0; $i -lt 90; $i++) {
        try { $probe = $client.GetAsync("$baseUrl/health").GetAwaiter().GetResult(); if ([int]$probe.StatusCode -eq 200) { $ready = $true; break } } catch { }
        Start-Sleep -Seconds 1
    }
    if (!$ready) { throw 'api_not_ready' }
    $phase = 'pipeline-pause'
    $null = Invoke-Api PUT '/api/v2/admin/collection/pipeline' @{ paused = $true; reason = 'Isolated full-weekend result verification' }
    $phase = 'cancel-inherited-active-tasks'
    $cancelled = 0; $targetResources = @{}
    $continueCancellationSweep = $true
    while ($continueCancellationSweep) {
        $phase = 'query-all-inherited-active-pages'
        $nonTargetTaskIds = @()
        for ($page = 1; $page -le 20; $page++) {
            $phase = 'fetch-inherited-active-page'
            $pageData = Get-Tasks $page $true
            $phase = 'read-inherited-active-page-items'
            $items = Get-Items $pageData
            $script:itemsCount = @($items).Count
            $script:firstItemType = if ($itemsCount -gt 0) { $items[0].GetType().Name } else { 'none' }
            $script:pageTotalCount = Get-Property $pageData 'totalCount'
            $phase = 'enumerate-inherited-active-page-items'
            for ($itemIndex = 0; $itemIndex -lt $itemsCount; $itemIndex++) {
                $task = $items[$itemIndex]
                $phase = 'read-active-task-status'
                $status = [string](Get-Property $task 'status')
                $phase = 'read-active-task-id'
                $taskId = [string](Get-Property $task 'taskId')
                $phase = 'read-active-task-resource'
                $resource = Get-Property $task 'resource'
                $resourceId = [string](Get-Property $resource 'id')
                $phase = 'classify-active-task-target'
                $isTarget = $resourceId -match '^2026092[67]:(Nakayama|Hanshin):(1[0-2]|[1-9])$'
                $isActive = $status -in @('Ready','Running','RetryWaiting') -or $status -in @('1','2','3')
                $phase = 'track-active-target-resource'
                if ($isTarget) { $targetResources[$resourceId] = $true }
                $phase = 'collect-non-target-task-id'
                if ($isActive -and $taskId -and !$isTarget) { $nonTargetTaskIds += $taskId }
            }
            $phase = 'inherited-page-items-enumerated'
        }
        if (!$nonTargetTaskIds.Count) {
            $continueCancellationSweep = $false
        } else {
            $phase = 'cancel-non-target-active-tasks'
            foreach ($taskId in $nonTargetTaskIds) {
                $null = Invoke-Api PATCH "/api/v2/admin/collection/tasks/$taskId" @{ cancellationRequested = $true }
                $cancelled++
            }
        }
    }
    $targetTaskCount = $targetResources.Count
    $phase = 'verify-active-task-and-queue-gates'
    $runtimeAudit = (& python (Join-Path $repo 'docs\changes\20260928_restore-fluentui-local-soak\audit-weekend-runtime.py') $runtime 2>$null | ConvertFrom-Json)
    if ($null -eq $runtimeAudit -or $runtimeAudit.auditFailed) { throw 'read_only_runtime_audit_failed' }
    $targetActive = [int]$runtimeAudit.targetTaskStatusCounts.Ready + [int]$runtimeAudit.targetTaskStatusCounts.Running + [int]$runtimeAudit.targetTaskStatusCounts.RetryWaiting
    $nonTargetActive = [int]$runtimeAudit.nonTargetActiveTasks
    $queueSnapshot = @{ queue_rows = [int]$runtimeAudit.localQueue.messageRows }
    if ($nonTargetActive -ne 0 -or $queueSnapshot.queue_rows -ne 0) { throw 'safety_gate_non_target_or_queue' }
    if ($QuiesceOnly) {
        if ($targetActive -ne 0) { throw 'safety_gate_target_still_active' }
        [ordered]@{ runDirectory=$runtime; quiesced=$true; targetActive=$targetActive; nonTargetActive=$nonTargetActive; queueRows=$queueSnapshot.queue_rows; cancelledNonTarget=$cancelled } | ConvertTo-Json -Compress
        return
    }
    if ($ResumeRuntime -and $targetActive -lt 1 -and !$RetryNakayama2 -and !$ResubmitMissingTargets -and !$RecollectPayoutAnomalies -and !$RetryPayoutAnomalies) { throw 'safety_gate_expected_target_work' }
    $submitted = 0
    $phase = if ($ResumeRuntime) { 'reuse-existing-weekend-receipts' } else { 'submit-48-weekend-races' }
    $courses = @(@{date='20260926';course='Nakayama'},@{date='20260926';course='Hanshin'},@{date='20260927';course='Nakayama'},@{date='20260927';course='Hanshin'})
    foreach ($item in $(if ($ResumeRuntime) { @() } else { $courses })) {
        $date = $item.date; $course = $item.course
        for ($number = 1; $number -le 12; $number++) {
            $race = '{0}:{1}:{2}' -f $date,$course,$number
            $phase = 'submit-' + $race
            $body = @{ mode='Resource'; resource=@{ resourceType=0; provider='JRA'; resourceId=$race; definitionId='race-detail'; requestedRevision=6; reason=5; lane=1; priority=50; effectiveDate=$date.Substring(0,4)+'-'+$date.Substring(4,2)+'-'+$date.Substring(6,2); attributes=@{course=$course;number=[string]$number} } }
            $null = Invoke-Api POST '/api/v2/admin/collection/tasks' $body; $submitted++
        }
    }
    if ($ResubmitMissingTargets) {
        $phase = 'resubmit-missing-result-races'
        $failedRaceIds = @($runtimeAudit.targetByRace.PSObject.Properties | Where-Object {
            [int]$_.Value.Failed.tasks -gt 0
        } | ForEach-Object { $_.Name })
        $missingRaces = @($runtimeAudit.domainReconciliation.missingResultRaces | Where-Object { $_ -notin $failedRaceIds })
        if ($MaxResubmitMissingTargets -gt 0) { $missingRaces = @($missingRaces | Select-Object -First $MaxResubmitMissingTargets) }
        foreach ($race in $missingRaces) {
            $parts = ([string]$race).Split(':')
            if ($parts.Count -ne 3 -or $parts[0] -notmatch '^2026092[67]$' -or $parts[1] -notin @('Nakayama','Hanshin') -or $parts[2] -notmatch '^(1[0-2]|[1-9])$') { throw 'invalid_audited_race_identity' }
            $date = $parts[0]; $course = $parts[1]; $number = [int]$parts[2]
            $resourceId = '{0}:{1}:{2}' -f $date,$course,$number
            $body = @{ mode='Resource'; resource=@{ resourceType=0; provider='JRA'; resourceId=$resourceId; definitionId='race-detail'; requestedRevision=6; reason=5; lane=1; priority=50; effectiveDate=('{0}-{1}-{2}' -f $date.Substring(0,4),$date.Substring(4,2),$date.Substring(6,2)); attributes=@{course=$course;number=[string]$number} } }
            $null = Invoke-Api POST '/api/v2/admin/collection/tasks' $body; $submitted++
        }
    }
    if ($RetryNakayama2) {
        $phase = 'submit-targeted-navigation-retry'
        $retryBody = @{ mode='Resource'; resource=@{ resourceType=0; provider='JRA'; resourceId='20260927:Nakayama:2'; definitionId='race-detail'; requestedRevision=6; reason=5; lane=1; priority=50; effectiveDate='2026-09-27'; attributes=@{course='Nakayama';number='2'} } }
        $null = Invoke-Api POST '/api/v2/admin/collection/tasks' $retryBody
    }
    if ($RecollectPayoutAnomalies) {
        $phase = 'recollect-modeled-payout-anomalies'
        $races = @($runtimeAudit.domainReconciliation.payoutAnomalies | ForEach-Object { $_.race } | Sort-Object -Unique)
        if (!$races.Count) {
            $races = @('20260926:Nakayama:3','20260926:Hanshin:6','20260926:Hanshin:11')
        }
        $anomalySet = @{}; foreach ($race in $races) { $anomalySet[[string]$race] = $true }
        $phase = 'cancel-prior-active-payout-recollections'
        for ($page = 1; $page -le 20; $page++) {
            $items = Get-Items (Get-Tasks $page $true)
            if (!$items.Count) { break }
            foreach ($task in $items) {
                $resource = Get-Property $task 'resource'; $resourceId = [string](Get-Property $resource 'id')
                if ($anomalySet.ContainsKey($resourceId)) {
                    $taskId = [string](Get-Property $task 'taskId')
                    if ($taskId) { $null = Invoke-Api PATCH "/api/v2/admin/collection/tasks/$taskId" @{ cancellationRequested = $true } }
                }
            }
        }
        $revisionResources = @()
        foreach ($race in $races) {
            $parts = ([string]$race).Split(':')
            if ($parts.Count -ne 3 -or $parts[0] -notmatch '^2026092[67]$' -or $parts[1] -notin @('Nakayama','Hanshin') -or $parts[2] -notmatch '^(1[0-2]|[1-9])$') { throw 'invalid_audited_race_identity' }
            $date = $parts[0]; $course = $parts[1]; $number = [int]$parts[2]
            $resourceId = '{0}:{1}:{2}' -f $date,$course,$number
            $revisionResources += @{ type=0; provider='JRA'; id=$resourceId }
        }
        $revisionResult = Invoke-Api POST '/api/v2/admin/collection/definitions/race-detail/revisions' @{
            revision=12; description='Isolated weekend verification: confirm persisted result refresh with nullable source identity'
            impact=@{ scopeType=1; resources=$revisionResources }
        }
        if ([int](Get-Property $revisionResult 'affected') -ne $races.Count) { throw 'revision_impact_count_mismatch' }
        foreach ($race in $races) {
            $parts = ([string]$race).Split(':')
            $date = $parts[0]; $course = $parts[1]; $number = [int]$parts[2]
            $resourceId = '{0}:{1}:{2}' -f $date,$course,$number
            $courseJra = if ($course -eq 'Nakayama') { '中山' } else { '阪神' }
            $raceAudit = $runtimeAudit.domainReconciliation.perRace.PSObject.Properties[$resourceId].Value
            $domainRaceId = [string](Get-Property $raceAudit 'raceId')
            if ([string]::IsNullOrWhiteSpace($domainRaceId)) { throw 'existing_domain_race_id_missing' }
            $body = @{ mode='Resource'; resource=@{ resourceType=0; provider='JRA'; resourceId=$resourceId; definitionId='race-detail'; requestedRevision=12; reason=5; lane=1; priority=50; effectiveDate=('{0}-{1}-{2}' -f $date.Substring(0,4),$date.Substring(4,2),$date.Substring(6,2)); attributes=@{course=$courseJra;number=[string]$number;domainRaceId=$domainRaceId} } }
            $null = Invoke-Api POST '/api/v2/admin/collection/tasks' $body; $submitted++
        }
    }
    if ($RetryPayoutAnomalies) {
        $phase = 'cancel-active-payout-anomaly-tasks'
        $anomalousRaces = @($runtimeAudit.domainReconciliation.payoutAnomalies | ForEach-Object { $_.race } | Sort-Object -Unique)
        $anomalousSet = @{}; foreach ($race in $anomalousRaces) { $anomalousSet[[string]$race] = $true }
        $cancelledAnomalyTasks = 0
        for ($page = 1; $page -le 20; $page++) {
            $pageData = Get-Tasks $page $true
            $items = Get-Items $pageData
            if (!$items.Count) { break }
            foreach ($task in $items) {
                $resource = Get-Property $task 'resource'; $resourceId = [string](Get-Property $resource 'id')
                $taskId = [string](Get-Property $task 'taskId')
                if ($anomalousSet.ContainsKey($resourceId) -and $taskId) {
                    $null = Invoke-Api PATCH "/api/v2/admin/collection/tasks/$taskId" @{ cancellationRequested = $true }
                    $cancelledAnomalyTasks++
                }
            }
        }
        $phase = 'resubmit-payout-anomalies-with-canonical-course'
        foreach ($race in $anomalousRaces) {
            $parts = ([string]$race).Split(':')
            $date = $parts[0]; $course = $parts[1]; $number = [int]$parts[2]
            $resourceId = '{0}:{1}:{2}' -f $date,$course,$number
            $courseJra = if ($course -eq 'Nakayama') { '中山' } else { '阪神' }
            $raceAudit = $runtimeAudit.domainReconciliation.perRace.PSObject.Properties[$resourceId].Value
            $domainRaceId = [string](Get-Property $raceAudit 'raceId')
            if ([string]::IsNullOrWhiteSpace($domainRaceId)) { throw 'existing_domain_race_id_missing' }
            $body = @{ mode='Resource'; resource=@{ resourceType=0; provider='JRA'; resourceId=$resourceId; definitionId='race-detail'; requestedRevision=12; reason=5; lane=1; priority=50; effectiveDate=('{0}-{1}-{2}' -f $date.Substring(0,4),$date.Substring(4,2),$date.Substring(6,2)); attributes=@{course=$courseJra;number=[string]$number;domainRaceId=$domainRaceId} } }
            $null = Invoke-Api POST '/api/v2/admin/collection/tasks' $body; $submitted++
        }
    }
    $env:ApiClient__BaseUrl = $baseUrl; $env:ApiClient__ApiKey = $key
    $env:LocalQueue__DatabasePath = Join-Path $runtime 'platform\local-collection-queue.db'
    $collectorAssembly = Join-Path $repo 'src\HorseRacingPrediction.Collector\bin\Release\net10.0\HorseRacingPrediction.Collector.dll'
    $collectorProcess = Start-Process dotnet -ArgumentList @($collectorAssembly,'--local-queue') -WorkingDirectory $repo -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runtime 'logs\collector.stdout.log') -RedirectStandardError (Join-Path $runtime 'logs\collector.stderr.log')
    Remove-Item Env:ApiClient__BaseUrl,Env:ApiClient__ApiKey,Env:LocalQueue__DatabasePath -ErrorAction SilentlyContinue
    $phase = 'resume-collection-pipeline'
    $null = Invoke-Api PUT '/api/v2/admin/collection/pipeline' @{ paused = $false }
    $phase = 'await-target-results'
    $deadline = [DateTimeOffset]::Now.AddHours(2); $terminal = $false; $pausedDuringWait = $false
    do {
        Start-Sleep -Seconds 20
        $counts = @{}
        $page = 1
        while ($true) {
            $items = Get-Items (Get-Tasks $page); if (!$items.Count) { break }
            foreach ($task in $items) {
                $resource = Get-Property $task 'resource'; $definition = Get-Property $task 'definition'
                $definitionId = if ($definition -is [string]) { $definition } else { [string](Get-Property $definition 'value') }
        if ($definitionId -eq 'race-detail' -and [string](Get-Property $resource 'id') -match '^2026092[67]:(Nakayama|Hanshin):(1[0-2]|[1-9])$') {
                    $status = [string](Get-Property $task 'status'); if ($statusMap.ContainsKey($status)) { $status = $statusMap[$status] }
                    if (!$counts.ContainsKey($status)) { $counts[$status] = 0 }; $counts[$status]++
                }
            }
            $pageData = Get-Tasks $page
            $total = [int](Get-Property $pageData 'totalCount')
            $pageSize = [int](Get-Property $pageData 'pageSize')
            if ($page * [Math]::Max(1,$pageSize) -ge $total) { break }; $page++
        }
        $active = ($counts['Ready'] + $counts['Running'] + $counts['RetryWaiting'])
        if ($active -eq 0 -and ($counts['Succeeded'] + $counts['Failed'] + $counts['Cancelled']) -ge 48) { $terminal = $true; break }
        $phase = 'check-pipeline-pause-state'
        $pipelineState = Invoke-Api GET '/api/v2/admin/collection/pipeline-state'
        if ((Get-Property $pipelineState 'isPaused') -eq $true) { $pausedDuringWait = $true; break }
        $phase = 'await-target-results'
        if (!$collectorProcess.HasExited -and [DateTimeOffset]::Now -gt $deadline) { break }
    } while ([DateTimeOffset]::Now -lt $deadline)
    if (!$pausedDuringWait) {
        $null = Invoke-Api PUT '/api/v2/admin/collection/pipeline' @{ paused = $true; reason = 'Weekend verification run complete' }
    }
    [ordered]@{ runDirectory=$runtime; submitted=$submitted; retryNakayama2=$RetryNakayama2.IsPresent; targetTasksSeen=$targetTaskCount; targetActiveBeforeResume=$targetActive; nonTargetActiveBeforeResume=$nonTargetActive; queueRowsBeforeResume=$queueSnapshot.queue_rows; cancelledNonTarget=$cancelled; terminal=$terminal; pausedDuringWait=$pausedDuringWait; finalRaceTaskCounts=$counts } | ConvertTo-Json -Depth 5 -Compress
} catch {
    $http = if ($_.Exception.Message -match '^api_status_(\d+)$') { [int]$Matches[1] } else { $null }
    $category = if ($http) { 'http_status_failure' } elseif ($_.Exception.Message -in @('api_not_ready','baseline_missing','baseline_file_missing','runtime_exists')) { $_.Exception.Message } else { 'operational_failure' }
    $exceptionNamespace = $_.Exception.GetType().Namespace
    $exceptionType = if ($exceptionNamespace -in @('System.Management.Automation','Microsoft.PowerShell.Commands')) { $exceptionNamespace + '.' + $_.Exception.GetType().Name } else { 'other' }
    $errorCategory = $_.CategoryInfo.Category.ToString()
    $commandName = if ($_.InvocationInfo.MyCommand.Name -in @('Get-Tasks','Get-Items','Get-Property','Invoke-Api','Start-Process','Get-Content','ConvertFrom-Json')) { $_.InvocationInfo.MyCommand.Name } else { 'other' }
    [ordered]@{ runDirectory=$runtime; failed=$true; phase=$phase; submitted=$submitted; httpStatus=$http; category=$category; exceptionType=$exceptionType; errorCategory=$errorCategory; command=$commandName; apiErrorCategory=$apiErrorCategory; apiValidationKeys=$apiValidationKeys; itemsShape=$itemsShape; itemsFailureType=$itemsFailureType; itemsCount=$itemsCount; pageTotalCount=$pageTotalCount; firstItemType=$firstItemType } | ConvertTo-Json -Compress
    exit 1
} finally {
    if ($collectorProcess -and !$collectorProcess.HasExited) { Stop-Process -Id $collectorProcess.Id -Force -ErrorAction SilentlyContinue }
    if ($apiProcess -and !$apiProcess.HasExited) { Stop-Process -Id $apiProcess.Id -Force -ErrorAction SilentlyContinue }
    if ($client) { $client.Dispose() }
    foreach ($name in @('ASPNETCORE_URLS','ApiKey__Key','ConnectionStrings__EventStore','CollectionPlatform__StateDirectory','CollectionOrchestration__BackgroundSchedulersEnabled','CollectionQueue__Enabled','CollectionQueue__Provider','CollectionQueue__LocalDatabasePath','PredictionScheduling__StateDirectory','DataProtection__KeysDirectory','ApiClient__BaseUrl','ApiClient__ApiKey','LocalQueue__DatabasePath')) { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
    $key = $null
}
