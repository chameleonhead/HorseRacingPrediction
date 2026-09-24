param([string]$Bash = 'C:\Program Files\Git\bin\bash.exe')
$ErrorActionPreference = 'Stop'
$workflow = Get-Content -Raw (Join-Path $PSScriptRoot '../../.github/workflows/app-deploy.yml')
$step = [regex]::Match($workflow, '(?s)- name: Migrate legacy race collection jobs.*?        run: \|\r?\n(?<script>.*?)\r?\n  deploy-collector-lambda:')
if (-not $step.Success) { throw 'Migration step not found' }
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
    */pipeline/resume) echo RESUMED >&2 ;;
    */pipeline/pause) if [ "$TEST_CASE" = pause-failure ]; then return 22; fi ;;
    */pipeline) if [ "$TEST_CASE" = get-failure ]; then return 22; fi; echo '{}' ;;
    */tasks\?*) echo '[]' ;;
    */failure-notifications\?*) if [ "$TEST_CASE" = new-failure ]; then echo '[{}]'; else echo '[]'; fi ;;
    */apply) if [ "$TEST_CASE" = apply-failure ]; then printf '{}\n409'; else printf '{}\n200'; fi ;;
    */preview) if [ "$TEST_CASE" = preview-failure ]; then return 22; fi; echo '{}' ;;
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
  elif [[ "$filter" == *'type == "array" and length == 0'* ]]; then
    local input
    input=$(cat)
    if [ "$input" = '[{}]' ]; then return 1; fi
    if [ "$TEST_CASE" = not-drained ]; then return 1; fi
    echo true
  elif [[ "$filter" == *'Invalid task list'* ]]; then
    if [ "$TEST_CASE" = invalid-tasks ]; then return 5; fi
    if [ "$TEST_CASE" = not-drained ]; then echo 1; else echo 0; fi
  elif [[ "$filter" == *sourceResources* ]] && [ "$TEST_CASE" = unconverged ]; then echo 1
  else echo 0
  fi
}
'@
$cases = @('running', 'paused', 'get-failure', 'invalid-state', 'invalid-original', 'not-drained', 'apply-failure', 'preview-failure', 'unconverged', 'new-failure')
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
if ($workflow.Contains('stop api || true') -or -not $workflow.Contains('Uncheckpointed SQLite WAL remains')) {
    throw 'Missing fail-closed backup guard'
}
if ($workflow -notmatch 'script: \|\r?\n            set -eu\r?\n            mkdir -p "\$APP_DIRECTORY/data"') {
    throw 'Remote restart must abort on failed commands'
}
