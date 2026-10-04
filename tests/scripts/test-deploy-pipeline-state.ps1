param([string]$Bash = 'C:\Program Files\Git\bin\bash.exe')
$ErrorActionPreference = 'Stop'
$workflow = Get-Content -Raw (Join-Path $PSScriptRoot '../../.github/workflows/app-deploy.yml')
if ($workflow.Contains('Migrate legacy race collection jobs') -or $workflow.Contains('/migrations/race-detail/')) {
    throw 'Obsolete race collection migration remains in deployment'
}
$step = [regex]::Match($workflow, '(?s)- name: Restore collection pipeline state after deployment.*?        run: \|\r?\n(?<script>.*?)\r?\n  deploy-collector-lambda:')
if (-not $step.Success) { throw 'Pipeline restoration step not found' }
$script = ($step.Groups['script'].Value -split '\r?\n' | ForEach-Object { $_ -replace '^          ', '' }) -join "`n"
$mocks = @'
set -euo pipefail
DOMAIN_NAME=example.test
LIGHTSAIL_HOST=unused
API_KEY=test-only
API_BASE_URL=https://example.test
GITHUB_OUTPUT=/dev/null
ORIGINAL_WAS_PAUSED=false
if [ "$TEST_CASE" = paused ]; then ORIGINAL_WAS_PAUSED=true; fi
if [ "$TEST_CASE" = invalid-original ]; then ORIGINAL_WAS_PAUSED=unknown; fi
sleep() { :; }
curl() {
  local url="${@: -1}"
  case "$url" in
    */pipeline-state)
      if [ "$TEST_CASE" = get-failure ]; then return 22; fi
      if [ "$TEST_CASE" = invalid-state ]; then echo '{"pipeline":{"isPaused":"invalid"}}'
      elif [ "$TEST_PHASE" = guard ] && [ "$TEST_CASE" != paused ]; then echo '{"pipeline":{"isPaused":false}}'
      else echo '{"pipeline":{"isPaused":true}}'
      fi ;;
    */api/admin/collection/pipeline) if [ "$TEST_CASE" = get-failure ]; then return 22; fi; echo '{}' ;;
    */pipeline)
      if [[ " $* " != *" -X PUT "* ]]; then return 90; fi
      if [[ " $* " == *'"pipeline":{"paused":true'* ]]; then
        if [ "$TEST_CASE" = pause-failure ]; then return 22; fi
      elif [[ " $* " == *'"pipeline":{"paused":false'* ]]; then
        echo RESUMED >&2
      else
        return 90
      fi ;;
    */tasks\?*)
      if [[ "$url" == *statuses=Running* ]]; then
        if [ "$TEST_CASE" = invalid-tasks ]; then echo '{"page":{"items":"invalid"}}'
        elif [ "$TEST_CASE" = not-drained ]; then echo '{"page":{"items":[{}]}}'
        else echo '{"page":{"items":[]}}'
        fi
      else echo '{"page":{"items":[]}}'; fi ;;
    */failure-notifications\?*) if [ "$TEST_CASE" = new-failure ]; then echo '{"notifications":[{}]}'; else echo '{"notifications":[]}'; fi ;;
    *) return 90 ;;
  esac
}
jq() {
  local filter="${@: -1}"
  local input
  input=$(cat)
  if [[ "$filter" == *'.pipeline.isPaused == true'* ]]; then
    [ "$input" = '{"pipeline":{"isPaused":true}}' ] || return 1
    echo true
  elif [[ "$filter" == *'.pipeline.isPaused |'* ]]; then
    case "$input" in
      '{"pipeline":{"isPaused":true}}') echo true ;;
      '{"pipeline":{"isPaused":false}}') echo false ;;
      *) return 5 ;;
    esac
  elif [[ "$filter" == *'.page.items | type == "array" and length == 0'* ]]; then
    [ "$input" = '{"page":{"items":[]}}' ] || return 1
    echo true
  elif [[ "$filter" == *'.page.items | if type == "array" then length'* ]]; then
    case "$input" in
      '{"page":{"items":[]}}') echo 0 ;;
      '{"page":{"items":[{}]}}') echo 1 ;;
      *) return 5 ;;
    esac
  elif [[ "$filter" == *'.notifications | type == "array" and length == 0'* ]]; then
    [ "$input" = '{"notifications":[]}' ] || return 1
    echo true
  else
    return 99
  fi
}
'@
$cases = @('running', 'paused', 'get-failure', 'invalid-state', 'invalid-original', 'not-drained', 'new-failure')
foreach ($case in $cases) {
    $payload = "export TEST_CASE='$case' TEST_PHASE='restore'`n$mocks`n$script"
    $output = $payload | & $Bash --noprofile --norc -s 2>&1
    $code = $LASTEXITCODE
    $resumed = ($output -join "`n") -match 'RESUMED'
    if ($resumed -ne ($case -eq 'running')) { throw "Incorrect resume: $case" }
    if (($code -eq 0) -ne ($case -in @('running', 'paused', 'new-failure'))) { throw "Incorrect exit: $case ($code)" }
    if ($case -eq 'new-failure' -and ($output -join "`n") -notmatch '::notice::Deployment succeeded; actionable collection failures remain, so the collection pipeline stays paused') {
        throw 'Actionable failures must be reported as a successful deployment with the collection pause stated.'
    }
    Write-Output "PASS $case"
}
$guard = [regex]::Match($workflow, '(?s)- id: collection-guard.*?        run: \|\r?\n(?<script>.*?)\r?\n      - name: Initialize collector infrastructure')
if (-not $guard.Success) { throw 'Guard step not found' }
$guardScript = ($guard.Groups['script'].Value -split '\r?\n' | ForEach-Object { $_ -replace '^          ', '' }) -join "`n"
foreach ($case in @('running', 'paused', 'get-failure', 'invalid-state', 'pause-failure', 'invalid-tasks', 'not-drained')) {
    $output = "export TEST_CASE='$case' TEST_PHASE='guard'`n$mocks`n$guardScript" | & $Bash --noprofile --norc -s 2>&1
    $code = $LASTEXITCODE
    if (($code -eq 0) -ne ($case -in @('running', 'paused'))) { throw "Incorrect guard exit: $case ($code)" }
    if (($output -join "`n") -match 'RESUMED') { throw 'Guard resumed collection' }
    Write-Output "PASS guard-$case"
}
if ($script -notmatch '/api/v2/admin/collection' -or
    $script -notmatch '/pipeline-state' -or
    $script -notmatch '/failure-notifications\?view=Actionable&limit=1' -or
    $script -notmatch '"\$base/tasks\?statuses=Running&pageSize=1"' -or
    $script -notmatch '\.pipeline\.isPaused' -or
    $script -notmatch '\.page\.items' -or
    $script -notmatch '\.notifications' -or
    $script -notmatch '-X PUT' -or $script -notmatch '\$base/pipeline' -or
    $script.Contains('/api/admin/collection') -or
    $script.Contains('/pipeline/resume')) {
    throw 'Post-deploy pipeline verification and resume must use the v2 routes and response shapes.'
}
$guardBase = [regex]::Match($guardScript, 'base="\$API_BASE_URL(?<path>/api/v2/admin/collection)"')
if (-not $guardBase.Success -or
    $guardScript -notmatch '"\$base/pipeline-state"' -or
    $guardScript -notmatch '"\$base/tasks\?statuses=Running&pageSize=1"' -or
    $guardScript -notmatch '-X PUT' -or
    $guardScript -notmatch '"pipeline":\{"paused":true' -or
    $guardScript -notmatch '\.pipeline\.isPaused' -or
    $guardScript -notmatch '\.page\.items \| if type == "array" then length' -or
    $guardScript.Contains('/api/admin/collection') -or
    $guardScript.Contains('/pipeline/pause')) {
    throw 'Pre-deploy pause/drain must use the v2 routes, payload, and response shapes.'
}
$migrationRoute = '/migration-previews/race-entry-owner-repair'
if (-not $workflow.Contains('/api/v2/admin/collection"') -or -not $workflow.Contains($migrationRoute) -or
    $workflow.Contains('/api/admin/collection/migrations/race-entry-owners/preview')) {
    throw 'Post-deploy race-entry owner preview must use its v2 route.'
}
if ($workflow.Contains('stop api || true') -or -not $workflow.Contains('Uncheckpointed SQLite WAL remains')) {
    throw 'Missing fail-closed backup guard'
}
if ($workflow -notmatch 'script: \|\r?\n            set -eu\r?\n            mkdir -p "\$APP_DIRECTORY/data"') {
    throw 'Remote restart must abort on failed commands'
}
# GitHub's pwsh wrapper propagates LASTEXITCODE; expected negative cases must not fail the step.
exit 0
