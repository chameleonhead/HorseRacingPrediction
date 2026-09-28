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
    */pipeline-state) if [ "$TEST_CASE" = get-failure ]; then return 22; fi; echo '{"isPaused":true}' ;;
    */pipeline/pause) if [ "$TEST_CASE" = pause-failure ]; then return 22; fi ;;
    */api/admin/collection/pipeline) if [ "$TEST_CASE" = get-failure ]; then return 22; fi; echo '{}' ;;
    */pipeline) if [[ " $* " == *" -X PUT "* ]]; then echo RESUMED >&2; else return 90; fi ;;
    */tasks\?*)
      if [[ "$url" == *status=Running* ]]; then echo '{"items":[]}'; else echo '[]'; fi ;;
    */failure-notifications\?*) if [ "$TEST_CASE" = new-failure ]; then echo '[{}]'; else echo '[]'; fi ;;
    *) return 90 ;;
  esac
}
jq() {
  # Stub jq outcomes; test the actual workflow shell's fail-closed control flow.
  local filter="${@: -1}"
  if [[ "$filter" == *'isPaused == true'* ]]; then
    if [ "$TEST_CASE" = invalid-state ]; then return 1; fi
    echo true
  elif [[ "$filter" == *isPaused* ]]; then
    case "$TEST_CASE" in
      invalid-state) return 5 ;;
      paused) echo true ;;
      *) echo false ;;
    esac
  elif [[ "$filter" == *'.items | type == "array" and length == 0'* ]]; then
    local input
    input=$(cat)
    if [ "$input" != '{"items":[]}' ] || [ "$TEST_CASE" = not-drained ]; then return 1; fi
    echo true
  elif [[ "$filter" == *'type == "array" and length == 0'* ]]; then
    local input
    input=$(cat)
    if [ "$input" = '[{}]' ]; then return 1; fi
    if [ "$TEST_CASE" = not-drained ]; then return 1; fi
    echo true
  elif [[ "$filter" == *'Invalid task list'* ]]; then
    if [ "$TEST_CASE" = invalid-tasks ]; then return 5; fi
    if [ "$TEST_CASE" = not-drained ]; then echo 1; else echo 0; fi
  else echo 0
  fi
}
'@
$cases = @('running', 'paused', 'get-failure', 'invalid-state', 'invalid-original', 'not-drained', 'new-failure')
foreach ($case in $cases) {
    $payload = "export TEST_CASE='$case'`n$mocks`n$script"
    $output = $payload | & $Bash --noprofile --norc -s 2>&1
    $code = $LASTEXITCODE
    $resumed = ($output -join "`n") -match 'RESUMED'
    if ($resumed -ne ($case -eq 'running')) { throw "Incorrect resume: $case" }
    if (($code -eq 0) -ne ($case -in @('running', 'paused'))) { throw "Incorrect exit: $case ($code)" }
    Write-Output "PASS $case"
}
$guard = [regex]::Match($workflow, '(?s)- id: collection-guard.*?        run: \|\r?\n(?<script>.*?)\r?\n      - name: Initialize collector infrastructure')
if (-not $guard.Success) { throw 'Guard step not found' }
$guardScript = ($guard.Groups['script'].Value -split '\r?\n' | ForEach-Object { $_ -replace '^          ', '' }) -join "`n"
foreach ($case in @('running', 'paused', 'get-failure', 'invalid-state', 'pause-failure', 'invalid-tasks', 'not-drained')) {
    $output = "export TEST_CASE='$case'`n$mocks`n$guardScript" | & $Bash --noprofile --norc -s 2>&1
    $code = $LASTEXITCODE
    if (($code -eq 0) -ne ($case -in @('running', 'paused'))) { throw "Incorrect guard exit: $case ($code)" }
    if (($output -join "`n") -match 'RESUMED') { throw 'Guard resumed collection' }
    Write-Output "PASS guard-$case"
}
if ($script -notmatch '/api/v2/admin/collection' -or
    $script -notmatch '/pipeline-state' -or
    $script -notmatch '/failure-notifications\?view=Actionable&limit=1' -or
    $script -notmatch '"\$base/tasks\?status=Running&limit=1"' -or
    $script -notmatch '-X PUT' -or $script -notmatch '\$base/pipeline' -or
    $script.Contains('/api/admin/collection') -or
    $script.Contains('/pipeline/resume')) {
    throw 'Post-deploy pipeline verification and resume must use the v2 routes and response shapes.'
}
$guardBase = [regex]::Match($guardScript, 'base="\$API_BASE_URL(?<path>/api/admin/collection)"')
if (-not $guardBase.Success -or $guardScript -notmatch '/pipeline/pause' -or $guardScript.Contains('/api/v2/admin/collection')) {
    throw 'Pre-deploy pause/drain must continue using v1 routes while v1 is active.'
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
