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
HORSE_ID_MIGRATION_OPERATION_ID="${HORSE_ID_MIGRATION_OPERATION_ID:-}"
if [ "$TEST_CASE" = paused ] || [ "$TEST_CASE" = migration-paused ]; then ORIGINAL_WAS_PAUSED=true; fi
if [ "$TEST_CASE" = invalid-original ]; then ORIGINAL_WAS_PAUSED=unknown; fi
if [ "$TEST_PHASE" = guard ]; then
  MOCK_PAUSED=false
  if [ "$TEST_CASE" = paused ] || [ "$TEST_CASE" = migration-paused ]; then MOCK_PAUSED=true; fi
else
  MOCK_PAUSED=true
fi
DATE_COUNTER=$(mktemp)
DATE_LOG=$(mktemp)
PUT_LOG=$(mktemp)
printf '0\n' > "$DATE_COUNTER"
if [[ "$TEST_CASE" == migration-* ]]; then
  trap 'printf "DATE_LOG="; tr "\\n" "," < "$DATE_LOG"; printf "PUT_LOG="; cat "$PUT_LOG"; rm -f "$DATE_COUNTER" "$DATE_LOG" "$PUT_LOG"' EXIT
else
  trap 'rm -f "$DATE_COUNTER" "$DATE_LOG" "$PUT_LOG"' EXIT
fi
sleep() { :; }
date() {
  if [ "${1:-}" = +%s ]; then
    local calls now
    calls=$(cat "$DATE_COUNTER")
    calls=$((calls + 1))
    printf '%s\n' "$calls" > "$DATE_COUNTER"
    if [ "$calls" -eq 1 ]; then now=10000; else now=10900; fi
    printf '%s\n' "$now" >> "$DATE_LOG"
    printf '%s\n' "$now"
  else
    command date "$@"
  fi
}
curl() {
  local url="${@: -1}"
  case "$url" in
    */pipeline-state)
      if [ "$TEST_CASE" = get-failure ]; then return 22; fi
      if [ "$TEST_CASE" = invalid-state ]; then echo '{"pipeline":{"isPaused":"invalid"}}'
      elif [ "$MOCK_PAUSED" = true ]; then echo '{"pipeline":{"isPaused":true}}'
      else echo '{"pipeline":{"isPaused":false}}'
      fi ;;
    */api/admin/collection/pipeline) if [ "$TEST_CASE" = get-failure ]; then return 22; fi; echo '{}' ;;
    */pipeline)
      if [[ " $* " != *" -X PUT "* ]]; then return 90; fi
      if [[ " $* " == *'"pipeline":{"paused":true'* ]]; then
        printf '%s\n' "$*" >> "$PUT_LOG"
        if [ "$TEST_CASE" = pause-failure ]; then return 22; fi
        MOCK_PAUSED=true
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
    $portablePayload = $payload.Replace("`r`n", "`n").Replace("`r", "`n")
    $output = $portablePayload | & $Bash --noprofile --norc -s 2>&1
    $code = $LASTEXITCODE
    $resumed = ($output -join "`n") -match 'RESUMED'
    if ($resumed -ne ($case -eq 'running')) { throw "Incorrect resume: $case; output=$(($output -join "`n"))" }
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
    $payload = "export TEST_CASE='$case' TEST_PHASE='guard'`n$mocks`n$guardScript"
    $portablePayload = $payload.Replace("`r`n", "`n").Replace("`r", "`n")
    $output = $portablePayload | & $Bash --noprofile --norc -s 2>&1
    $code = $LASTEXITCODE
    if (($code -eq 0) -ne ($case -in @('running', 'paused'))) { throw "Incorrect guard exit: $case ($code)" }
    if (($output -join "`n") -match 'RESUMED') { throw 'Guard resumed collection' }
    Write-Output "PASS guard-$case"
}
foreach ($case in @('migration-unpaused', 'migration-paused', 'migration-empty-guid')) {
    $guid = if ($case -eq 'migration-empty-guid') { '00000000-0000-0000-0000-000000000000' } else { '00000000-0000-0000-0000-000000000001' }
    $payload = "export TEST_CASE='$case' TEST_PHASE='guard' GITHUB_EVENT_NAME='workflow_dispatch' HORSE_ID_MIGRATION_OPERATION_ID='$guid'`n$mocks`n$guardScript`n" + @'
printf 'DATE_LOG='
tr '\n' ',' < "$DATE_LOG"
printf 'PUT_LOG='
cat "$PUT_LOG"
'@
    $portablePayload = $payload.Replace("`r`n", "`n").Replace("`r", "`n")
    $output = $portablePayload | & $Bash --noprofile --norc -s 2>&1
    $code = $LASTEXITCODE
    $text = $output -join "`n"
    $expectedCode = switch ($case) { 'migration-unpaused' { 1 } 'migration-empty-guid' { 2 } default { 0 } }
    if ($code -ne $expectedCode) { throw "Migration pause guard had wrong exit for $case ($code, expected $expectedCode): $text" }
    $putCount = ([regex]::Matches($text, '-X PUT')).Count
    $expectedPutCount = 0
    if ($putCount -ne $expectedPutCount) { throw "Unexpected pause writes for ${case}: count=$putCount expected=$expectedPutCount; $text" }
    if ($case -eq 'migration-paused' -and $text -notmatch 'DATE_LOG=10000,10900,') { throw "Migration quiescence did not use the confirmed-pause timestamp + 900 seconds for ${case}: $text" }
    if ($case -in @('migration-unpaused','migration-empty-guid') -and $text -notmatch 'DATE_LOG=PUT_LOG=') { throw "Invalid migration request must reject before pausing or starting the quiescence wait: $text" }
    if ($text -match 'RESUMED' -or ($case -eq 'migration-paused' -and $text -match 'deployment version transition')) {
        throw "Migration mode overwrote an existing operator pause or resumed collection: $text"
    }
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
$bashStart = $workflow.IndexOf("bash -s <<'HORSE_ID_MIGRATION_REMOTE'", [StringComparison]::Ordinal)
$helperSource = $workflow.IndexOf('. "$APP_DIRECTORY/horse-id-migration-deploy.sh"', $bashStart, [StringComparison]::Ordinal)
$toolCheck = $workflow.IndexOf('horse_id_migration_require_tools', $helperSource, [StringComparison]::Ordinal)
$firstMutation = $workflow.IndexOf('mkdir -p "$APP_DIRECTORY/data"', $toolCheck, [StringComparison]::Ordinal)
if ($bashStart -lt 0 -or $helperSource -le $bashStart -or $toolCheck -le $helperSource -or $firstMutation -le $toolCheck) {
    throw 'Remote restart must source the migration helper and validate dependencies before mutations'
}
# GitHub's pwsh wrapper propagates LASTEXITCODE; expected negative cases must not fail the step.
exit 0
