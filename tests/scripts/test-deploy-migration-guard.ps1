param(
    [string] $Bash = 'C:\Program Files\Git\bin\bash.exe',
    [string] $Python = 'python3'
)
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$helperPath = (Resolve-Path (Join-Path $repositoryRoot 'deploy/horse-id-migration-deploy.sh')).Path
$workflowPath = Join-Path $repositoryRoot '.github/workflows/app-deploy.yml'
$dockerfilePath = Join-Path $repositoryRoot 'Dockerfile'
$workflow = Get-Content -Raw -LiteralPath $workflowPath
$dockerfile = Get-Content -Raw -LiteralPath $dockerfilePath
$helper = Get-Content -Raw -LiteralPath $helperPath

function ConvertTo-BashPath([string] $Path) {
    if ($IsWindows) {
        $gitBin = Split-Path $Bash -Parent
        $cygpath = Join-Path $gitBin 'cygpath.exe'
        if (-not (Test-Path -LiteralPath $cygpath)) {
            $cygpath = Join-Path (Split-Path $gitBin -Parent) 'usr/bin/cygpath.exe'
        }
        if (-not (Test-Path -LiteralPath $cygpath)) { throw "Git Bash cygpath not found beside $Bash" }
        return (& $cygpath -u $Path).Trim()
    }
    return $Path
}

function Quote-Bash([string] $Value) {
    $replacement = "'" + '"' + "'" + '"' + "'"
    return "'" + $Value.Replace("'", $replacement) + "'"
}

function Invoke-Bash([string] $Payload, [int] $TimeoutMilliseconds = 15000) {
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo.FileName = $Bash
    $process.StartInfo.ArgumentList.Add('--noprofile')
    $process.StartInfo.ArgumentList.Add('--norc')
    $process.StartInfo.ArgumentList.Add('-s')
    $process.StartInfo.WorkingDirectory = $repositoryRoot
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardInput = $true
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    if (-not $process.Start()) { throw 'Could not start Bash test process.' }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    try {
        $portablePayload = $Payload.Replace("`r`n", "`n").Replace("`r", "`n")
        $process.StandardInput.Write($portablePayload)
        $process.StandardInput.Close()
        if (-not $process.WaitForExit($TimeoutMilliseconds)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "Bash child exceeded ${TimeoutMilliseconds}ms; its process tree was terminated."
        }
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output = ($stdoutTask.Result + $stderrTask.Result)
        }
    }
    finally {
        $process.Dispose()
    }
}

function Assert-Case([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

$pythonCommand = (Get-Command $Python -ErrorAction Stop).Source
$ruby = Get-Command ruby -ErrorAction SilentlyContinue
if ($ruby) {
    $yamlSemanticScript = @'
require 'yaml'
workflow = YAML.safe_load_file(ARGV.fetch(0), aliases: false)
events = workflow['on'] || workflow[true]
raise 'workflow_dispatch input is not optional string' unless events.dig('workflow_dispatch','inputs','horse_id_migration_operation_id').then { |v| v && v['required'] == false && v['type'] == 'string' }
raise 'recovery input contract is missing' unless events.dig('workflow_dispatch','inputs','api_startup_recovery') == {'description'=>'Restore the API only from the expected blocked deployment; leaves collection paused','required'=>false,'default'=>false,'type'=>'boolean'} && events.dig('workflow_dispatch','inputs','expected_deployed_sha') == {'description'=>'Exact currently deployed API commit SHA required for API startup recovery','required'=>false,'type'=>'string'}
raise 'verification runner must be Ubuntu 24.04' unless workflow.dig('jobs','verify','runs-on') == 'ubuntu-24.04'
verify_steps = workflow.dig('jobs','verify','steps')
guard_step = verify_steps.find { |s| s['name'] == 'Verify horse identity migration deploy guard' }
raise 'migration guard tests need a finite step timeout' unless guard_step && guard_step['timeout-minutes'] == 3
raise 'build must depend on verification' unless Array(workflow.dig('jobs','build-and-push','needs')).include?('verify')
early_validation = verify_steps.find { |s| s['name'] == 'Validate optional migration request before publishing or deployment' }
raise 'pre-publish validation must reject GUID.Empty' unless early_validation && early_validation['run'].include?('00000000-0000-0000-0000-000000000000')
raise 'recovery input validation must be default-off, manual-only, SHA-bound, and migration-exclusive' unless early_validation['run'].include?('API_STARTUP_RECOVERY') && early_validation['run'].include?('EXPECTED_DEPLOYED_SHA') && early_validation['run'].include?('workflow_dispatch') && early_validation['run'].include?('HORSE_ID_MIGRATION_OPERATION_ID')
recovery = workflow.dig('jobs','recover-api-startup')
raise 'API recovery must be a manual API-only route after verified build' unless recovery && recovery['if'].to_s.include?('api_startup_recovery') && Array(recovery['needs']).sort == ['build-and-push','verify'] && recovery['environment'] == 'production' && recovery['timeout-minutes'] == 50
raise 'normal deploy paths must exclude API recovery without changing their requirements' unless workflow.dig('jobs','deploy')['if'].to_s.include?('api_startup_recovery') && workflow.dig('jobs','deploy-collector-lambda')['if'].to_s.include?('api_startup_recovery')
upload = recovery['steps'].find { |s| s['name'] == 'Upload only the verified recovery helper' }
recovery_step_index = recovery['steps'].index { |s| s['name'] == 'Recover only the exact blocked API image' }
raise 'recovery must upload only the helper from the checked-out exact SHA before invoking it' unless upload && upload['uses'] == 'appleboy/scp-action@v1.0.0' && upload.dig('with','source') == 'deploy/horse-id-migration-deploy.sh' && upload.dig('with','strip_components') == 1 && recovery_step_index && recovery['steps'].index(upload) < recovery_step_index
recovery_script = recovery.dig('steps').find { |s| s['name'] == 'Recover only the exact blocked API image' }.dig('with','script')
fixed_success_call = '( horse_id_migration_run_fixed_command "$verified_success_guid" "$target_image" "$APP_DIRECTORY/.env" )'
recovery_lines = recovery_script.lines.map(&:strip)
fixed_branch = recovery_lines.index('if [ -n "$verified_success_guid" ]; then')
raise 'recovery must execute the fixed-success verifier exactly once only for a nonempty GUID' unless recovery_script.scan('horse_id_migration_run_fixed_command').length == 1 && recovery_script.split(fixed_success_call, -1).length - 1 == 1 && fixed_branch && recovery_lines[fixed_branch + 1] == fixed_success_call && recovery_lines[fixed_branch + 2] == 'fi'
recovery_order = ['expected_current_image=', 'horse_id_migration_assert_api_startup_blocked', 'docker compose --env-file .env pull api', 'horse_id_migration_check_image_capability', 'recovery_started=true', 'docker compose --env-file .env stop api', 'horse_id_migration_configured_store_paths', 'horse_id_migration_acquire_source_ownership', 'horse_id_migration_acquire_journal_operation_lock', 'horse_id_migration_assert_recovery_collection_state', 'sleep 900', 'horse_id_migration_assert_recovery_collection_state', 'horse_id_migration_journal_preflight', 'horse_id_migration_copy_store_set', 'RECOVERY-METADATA', 'horse_id_migration_update_recovery_env', 'horse_id_migration_release_source_ownership', fixed_success_call, 'docker compose --env-file .env up -d --no-deps --force-recreate api', 'horse_id_migration_wait_recovery_swagger', 'horse_id_migration_assert_recovery_api_ready']
recovery_positions = recovery_order.each_with_object([]) do |needle, positions|
  positions << recovery_script.index(needle, positions.last || 0)
end
backup_i = recovery_script.index('horse_id_migration_copy_store_set')
release_i = recovery_script.index('horse_id_migration_release_source_ownership', backup_i)
raise 'API recovery must verify, stop, hold real ownership through full quiescence and backup, then update/preserve configuration, verify prior receipt in an isolated child, and start/read back exact API' unless recovery_positions.all? && recovery_positions.each_cons(2).all? { |a,b| a < b } && backup_i < release_i && release_i < recovery_script.index('( horse_id_migration_run_fixed_command')
cleanup_body = recovery_script[recovery_script.index('recovery_cleanup()')...recovery_script.index('trap recovery_cleanup EXIT')]
raise 'recovery failure cleanup must stop the API before releasing held ownership' unless cleanup_body.index('docker compose --env-file .env stop api') < cleanup_body.index('horse_id_migration_release_source_ownership')
raise 'API recovery must not change Lambda, Terraform, queue, resume, or external credential settings' if recovery_script.match?(/terraform|lambda|sqs|resume|AWS_ROLE_TO_ASSUME|GITHUB_TOKEN/i)
deploy = workflow.dig('jobs','deploy')
raise 'deploy must wait for the collector Lambda transition' unless Array(deploy['needs']).include?('deploy-collector-lambda')
steps = deploy['steps']
idx = ->(name) { steps.index { |s| s['name'] == name } }
remote_i, health_i, receipt_i, failure_i, restore_i = ['Restart remote stack', 'Verify deployment health', 'Verify migration receipt and running image after restart', 'Keep API stopped after failed migration deployment', 'Restore collection pipeline state after deployment'].map { |n| idx.call(n) }
raise 'deploy failure/success steps are out of order' unless remote_i && health_i && receipt_i && failure_i && restore_i && remote_i < health_i && health_i < receipt_i && receipt_i < failure_i && failure_i < restore_i
raise 'deploy job/remote deadlines are missing' unless deploy['timeout-minutes'] == 50 && steps[remote_i]['timeout-minutes'] == 40 && steps[remote_i].dig('with','command_timeout') == '38m'
remote = steps[remote_i].dig('with','script')
ordered = ['horse_id_migration_require_tools','docker compose --env-file .env pull','horse_id_migration_check_image_capability','horse_id_migration_configured_store_paths','horse_id_migration_assert_api_paused','docker compose --env-file .env stop api','horse_id_migration_journal_preflight','horse_id_migration_run_fixed_command "$verified_success_guid"','horse_id_migration_copy_store_set','horse_id_migration_run_fixed_command "$HORSE_ID_MIGRATION_OPERATION_ID"','docker compose --env-file .env up']
positions = ordered.map { |needle| remote.index(needle) }
raise 'remote migration operations are missing or out of order' unless positions.all? && positions.each_cons(2).all? { |a,b| a < b }
raise 'receipt check must be migration-only and bounded' unless steps[receipt_i]['if'].to_s.include?('horse_id_migration_operation_id') && steps[receipt_i]['timeout-minutes'] == 3
receipt_script = steps[receipt_i].dig('with','script')
fixed_receipt = receipt_script.index('horse_id_migration_run_fixed_command "$HORSE_ID_MIGRATION_OPERATION_ID"')
integrity_check = receipt_script.index('horse_id_migration_check_configured_store_integrity "$APP_DIRECTORY/data"')
raise 'postrestart integrity must follow the successful fixed receipt exactly once' unless fixed_receipt && integrity_check && fixed_receipt < integrity_check && receipt_script.scan('horse_id_migration_check_configured_store_integrity').length == 1
raise 'integrity helper must only be called from the migration-only receipt step' unless workflow.to_s.scan('horse_id_migration_check_configured_store_integrity').length == 1
raise 'failure stop must be migration-only' unless steps[failure_i]['if'].to_s.include?('failure()') && steps[failure_i]['if'].to_s.include?('horse_id_migration_operation_id')
collector = workflow.dig('jobs','deploy-collector-lambda')
guard = collector['steps'].find { |s| s['id'] == 'collection-guard' }
raise 'collection guard deadline is missing' unless guard && guard['timeout-minutes'] == 27
guard_script = guard['run']
empty_guard = guard_script.index('"$was_paused" != "true"')
put_call = guard_script.index('-X PUT')
quiescent = guard_script.index('pause_confirmed_at + 900')
recheck = guard_script.index('final_pause_state=')
raise 'migration must require existing pause before any write' unless empty_guard && put_call && empty_guard < put_call
raise 'migration must reconfirm pause after 900 seconds' unless quiescent && recheck && quiescent < recheck && guard_script.index('final_running=', recheck)
collector_validation = collector['steps'].find { |s| s['name'] == 'Validate optional migration request before AWS or infrastructure changes' }
raise 'pre-AWS validation must reject GUID.Empty' unless collector_validation && collector_validation['run'].include?('00000000-0000-0000-0000-000000000000')
lambda_i = collector['steps'].index { |s| s['name'] == 'Verify deployed collector Lambda is active and updated' }
raise 'Lambda image verification is not conditional/bounded' unless lambda_i && collector['steps'][lambda_i]['if'].to_s.include?('horse_id_migration_operation_id') && collector['steps'][lambda_i]['timeout-minutes'] == 16 && collector['steps'][lambda_i]['run'].include?('Configuration.LastUpdateStatus') && collector['steps'][lambda_i]['run'].include?('Code.ImageUri')
puts 'PASS parsed workflow semantics'
'@
    $yamlResult = & $ruby.Source -e $yamlSemanticScript $workflowPath 2>&1
    Assert-Case ($LASTEXITCODE -eq 0) "Parsed workflow semantic validation failed: $(($yamlResult -join "`n"))"
}
else {
    $yamlSemanticScript = @'
import sys,yaml
w=yaml.safe_load(open(sys.argv[1],encoding='utf-8'))
events=w.get('on',w.get(True))
assert events['workflow_dispatch']['inputs']['horse_id_migration_operation_id']=={'description':'Existing staged horse-identity migration GUID; leave empty for a normal deploy','required':False,'type':'string'}
assert events['workflow_dispatch']['inputs']['api_startup_recovery']=={'description':'Restore the API only from the expected blocked deployment; leaves collection paused','required':False,'default':False,'type':'boolean'}
assert events['workflow_dispatch']['inputs']['expected_deployed_sha']=={'description':'Exact currently deployed API commit SHA required for API startup recovery','required':False,'type':'string'}
assert w['jobs']['verify']['runs-on']=='ubuntu-24.04'
verify=w['jobs']['verify']['steps']
guard_test=next(s for s in verify if s.get('name')=='Verify horse identity migration deploy guard')
assert guard_test['timeout-minutes']==3 and 'verify' in w['jobs']['build-and-push']['needs']
early_validation=next(s for s in verify if s.get('name')=='Validate optional migration request before publishing or deployment')
assert '00000000-0000-0000-0000-000000000000' in early_validation['run']
assert all(x in early_validation['run'] for x in ('API_STARTUP_RECOVERY','EXPECTED_DEPLOYED_SHA','workflow_dispatch','HORSE_ID_MIGRATION_OPERATION_ID'))
recovery=w['jobs']['recover-api-startup']; assert 'api_startup_recovery' in recovery['if'] and sorted(recovery['needs'])==['build-and-push','verify'] and recovery['environment']=='production' and recovery['timeout-minutes']==50
assert 'api_startup_recovery' in w['jobs']['deploy']['if'] and 'api_startup_recovery' in w['jobs']['deploy-collector-lambda']['if']
upload=next((s for s in recovery['steps'] if s.get('name')=='Upload only the verified recovery helper'),None); recovery_step_index=next((i for i,s in enumerate(recovery['steps']) if s.get('name')=='Recover only the exact blocked API image'),None)
assert upload and upload['uses']=='appleboy/scp-action@v1.0.0' and upload['with']['source']=='deploy/horse-id-migration-deploy.sh' and upload['with']['strip_components']==1 and recovery_step_index is not None and recovery['steps'].index(upload)<recovery_step_index
recovery_script=next(s for s in recovery['steps'] if s.get('name')=='Recover only the exact blocked API image')['with']['script']
fixed_success_call='( horse_id_migration_run_fixed_command "$verified_success_guid" "$target_image" "$APP_DIRECTORY/.env" )'
assert recovery_script.count('horse_id_migration_run_fixed_command')==1 and recovery_script.count(fixed_success_call)==1
recovery_lines=recovery_script.splitlines(); fixed_branch=next(i for i,line in enumerate(recovery_lines) if line.strip()=='if [ -n "$verified_success_guid" ]; then')
assert recovery_lines[fixed_branch+1].strip()==fixed_success_call and recovery_lines[fixed_branch+2].strip()=='fi'
recovery_order=('expected_current_image=','horse_id_migration_assert_api_startup_blocked','docker compose --env-file .env pull api','horse_id_migration_check_image_capability','recovery_started=true','docker compose --env-file .env stop api','horse_id_migration_configured_store_paths','horse_id_migration_acquire_source_ownership','horse_id_migration_acquire_journal_operation_lock','horse_id_migration_assert_recovery_collection_state','sleep 900','horse_id_migration_assert_recovery_collection_state','horse_id_migration_journal_preflight','horse_id_migration_copy_store_set','RECOVERY-METADATA','horse_id_migration_update_recovery_env','horse_id_migration_release_source_ownership',fixed_success_call,'docker compose --env-file .env up -d --no-deps --force-recreate api','horse_id_migration_wait_recovery_swagger','horse_id_migration_assert_recovery_api_ready')
recovery_positions=[]
for n in recovery_order:
    recovery_positions.append(recovery_script.index(n,recovery_positions[-1] if recovery_positions else 0))
assert recovery_positions==sorted(recovery_positions)
backup_i=recovery_script.index('horse_id_migration_copy_store_set'); release_i=recovery_script.index('horse_id_migration_release_source_ownership',backup_i)
assert backup_i<release_i<recovery_script.index('( horse_id_migration_run_fixed_command')
cleanup_body=recovery_script[recovery_script.index('recovery_cleanup()'):recovery_script.index('trap recovery_cleanup EXIT')]
assert cleanup_body.index('docker compose --env-file .env stop api')<cleanup_body.index('horse_id_migration_release_source_ownership')
assert not any(x in recovery_script.lower() for x in ('terraform','lambda','sqs','resume','aws_role_to_assume','github_token'))
deploy=w['jobs']['deploy']
assert 'deploy-collector-lambda' in deploy['needs'] and deploy['timeout-minutes']==50
steps=deploy['steps']; names=[s.get('name') for s in steps]
indexes=[names.index(n) for n in ('Restart remote stack','Verify deployment health','Verify migration receipt and running image after restart','Keep API stopped after failed migration deployment','Restore collection pipeline state after deployment')]
assert indexes==sorted(indexes) and steps[indexes[0]]['timeout-minutes']==40 and steps[indexes[0]]['with']['command_timeout']=='38m'
script=steps[indexes[0]]['with']['script']; needles=('horse_id_migration_require_tools','docker compose --env-file .env pull','horse_id_migration_check_image_capability','horse_id_migration_configured_store_paths','horse_id_migration_assert_api_paused','docker compose --env-file .env stop api','horse_id_migration_journal_preflight','horse_id_migration_run_fixed_command "$verified_success_guid"','horse_id_migration_copy_store_set','horse_id_migration_run_fixed_command "$HORSE_ID_MIGRATION_OPERATION_ID"','docker compose --env-file .env up')
positions=[script.index(n) for n in needles]; assert positions==sorted(positions)
receipt_script=steps[indexes[2]]['with']['script']; fixed_receipt=receipt_script.index('horse_id_migration_run_fixed_command "$HORSE_ID_MIGRATION_OPERATION_ID"'); integrity_check=receipt_script.index('horse_id_migration_check_configured_store_integrity "$APP_DIRECTORY/data"')
assert fixed_receipt<integrity_check and receipt_script.count('horse_id_migration_check_configured_store_integrity')==1 and w['jobs']['deploy']['steps'][indexes[2]]['if'].find('horse_id_migration_operation_id')>=0
assert sum(s.get('with',{}).get('script','').count('horse_id_migration_check_configured_store_integrity') for s in steps)==1
assert 'horse_id_migration_operation_id' in steps[indexes[2]]['if'] and steps[indexes[2]]['timeout-minutes']==3
assert 'failure()' in steps[indexes[3]]['if'] and 'horse_id_migration_operation_id' in steps[indexes[3]]['if']
collector=w['jobs']['deploy-collector-lambda']; guard=next(s for s in collector['steps'] if s.get('id')=='collection-guard'); text=guard['run']
assert guard['timeout-minutes']==27 and text.index('"$was_paused" != "true"')<text.index('-X PUT')
assert text.index('pause_confirmed_at + 900')<text.index('final_pause_state=')<text.index('final_running=')
collector_validation=next(s for s in collector['steps'] if s.get('name')=='Validate optional migration request before AWS or infrastructure changes')
assert '00000000-0000-0000-0000-000000000000' in collector_validation['run']
lambda_step=next(s for s in collector['steps'] if s.get('name')=='Verify deployed collector Lambda is active and updated')
assert 'horse_id_migration_operation_id' in lambda_step['if'] and lambda_step['timeout-minutes']==16 and 'Configuration.LastUpdateStatus' in lambda_step['run'] and 'Code.ImageUri' in lambda_step['run']
print('PASS parsed workflow semantics')
'@
    $yamlResult = & $pythonCommand -c $yamlSemanticScript $workflowPath 2>&1
    Assert-Case ($LASTEXITCODE -eq 0) "Parsed workflow semantic validation requires Ruby or PyYAML: $(($yamlResult -join "`n"))"
}

$pythonBashPath = ConvertTo-BashPath $pythonCommand
$helperBashPath = ConvertTo-BashPath $helperPath
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("hrp-migration-deploy-test-" + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $tempRoot)
$tempBashPath = ConvertTo-BashPath $tempRoot
$recoveryStateSeed = @'
import sqlite3,sys
path,version,control,pause,task,lease=sys.argv[1:]
db=sqlite3.connect(path)
db.executescript('''
CREATE TABLE collection_schema_history(version INTEGER NOT NULL PRIMARY KEY, applied_at TEXT NOT NULL);
CREATE TABLE collection_platform_controls(ControlId TEXT NOT NULL PRIMARY KEY, IsPaused INTEGER NOT NULL);
CREATE TABLE collection_tasks(Status TEXT NOT NULL);
CREATE TABLE collection_execution_leases(Status TEXT NOT NULL);
''')
db.execute('INSERT INTO collection_schema_history VALUES (?,?)',(int(version),'2026-10-24T00:00:00Z'))
if control != 'missing': db.execute('INSERT INTO collection_platform_controls VALUES (?,?)',('pipeline',int(pause)))
if task != 'empty': db.execute('INSERT INTO collection_tasks VALUES (?)',(task,))
if lease != 'empty': db.execute('INSERT INTO collection_execution_leases VALUES (?)',(lease,))
db.commit();db.close()
'@

function New-RecoveryStateFixture([string] $Name, [string] $Version = '25', [string] $Control = 'present', [string] $Pause = '1', [string] $Task = 'empty', [string] $Lease = 'empty') {
    $path = Join-Path $tempRoot "$Name.db"
    & $pythonCommand -c $recoveryStateSeed $path $Version $Control $Pause $Task $Lease
    if ($LASTEXITCODE -ne 0) { throw "Could not seed recovery state fixture $Name." }
    return $path
}

function Test-RecoveryState([string] $Path, [bool] $Expected) {
    $before = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    $pathBash = ConvertTo-BashPath $Path
    $result = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
export HRP_PYTHON3=$(Quote-Bash $pythonBashPath)
horse_id_migration_assert_recovery_collection_state $(Quote-Bash $pathBash)
"@
    if ($Expected) {
        Assert-Case ($result.ExitCode -eq 0 -and $result.Output -match 'PASS code=RECOVERY_QUIESCENT') "Valid paused collection state must pass read-only inspection. $($result.Output)"
    }
    else {
        Assert-Case ($result.ExitCode -ne 0) "Unsafe or unknown collection state must fail closed. $($result.Output)"
        Assert-Case ($result.Output -notmatch [regex]::Escape($Path) -and $result.Output -notmatch 'Traceback|sqlite3\.') 'Collection state failures must not expose paths or SQLite internals.'
    }
    $after = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    Assert-Case ($before -eq $after) 'Recovery state inspection must leave SQLite bytes unchanged.'
    return $result
}

function Write-OwnershipMarkers([string] $Source, [string] $Logical, [string] $RuntimeGate, [string] $ControlGate) {
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Logical)))
    [IO.File]::WriteAllText($RuntimeGate, "HorseIdentityMigrationSourceOwnership:v1:$hash`n", [Text.Encoding]::ASCII)
    [IO.File]::WriteAllText($ControlGate, "HorseIdentityMigrationSupervisorControl:v1:$hash`n", [Text.Encoding]::ASCII)
}

function Invoke-SourceOwnership([string] $SourceBash, [string] $Logical, [bool] $Expected) {
    $result = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
export HRP_PYTHON3=$(Quote-Bash $pythonBashPath)
if horse_id_migration_acquire_source_ownership $(Quote-Bash $SourceBash) $(Quote-Bash $Logical); then
  horse_id_migration_release_source_ownership
  exit 0
fi
exit 90
"@
    if ($Expected) { Assert-Case ($result.ExitCode -eq 0) "Valid existing ownership files must pass. $($result.Output)" }
    else { Assert-Case ($result.ExitCode -eq 90) "Invalid ownership files must fail without helper setup. $($result.Output)" }
    return $result
}

function Invoke-JournalOperationLock([string] $JournalBash, [bool] $Expected) {
    $result = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
if horse_id_migration_acquire_journal_operation_lock $(Quote-Bash $JournalBash); then
  horse_id_migration_release_source_ownership
  exit 0
fi
exit 90
"@
    if ($Expected) { Assert-Case ($result.ExitCode -eq 0) "Existing empty journal lock must interoperate. $($result.Output)" }
    else { Assert-Case ($result.ExitCode -eq 90) "Missing, corrupt, or aliased journal lock must fail closed. $($result.Output)" }
    return $result
}

function Invoke-RecoveryEnvUpdate([string] $Path, [string] $ImageName, [string] $ImageTag) {
    $pathBash = ConvertTo-BashPath $Path
    Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
export HRP_PYTHON3=$(Quote-Bash $pythonBashPath)
horse_id_migration_update_recovery_env $(Quote-Bash $pathBash) $(Quote-Bash $ImageName) $(Quote-Bash $ImageTag)
"@
}

$seedPython = @'
import sqlite3, sys
path, mode, count = sys.argv[1], sys.argv[2], int(sys.argv[3])
db = sqlite3.connect(path)
db.executescript('''
CREATE TABLE migration_operations (
 operation_id TEXT PRIMARY KEY NOT NULL, request_hash TEXT NOT NULL, request_json TEXT NOT NULL,
 status INTEGER NOT NULL, reason INTEGER NOT NULL, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL,
 terminal_utc TEXT NULL, results_json TEXT NOT NULL, workflow_claimed INTEGER NOT NULL DEFAULT 0);
CREATE TABLE migration_pages (operation_id TEXT NOT NULL, page_identity TEXT NOT NULL, surface INTEGER NOT NULL,
 table_name TEXT NOT NULL, row_count INTEGER NOT NULL, membership_hash TEXT NOT NULL, before_hash TEXT NOT NULL,
 expected_after_hash TEXT NOT NULL, state INTEGER NOT NULL, updated_utc TEXT NOT NULL,
 PRIMARY KEY(operation_id,page_identity));
CREATE TABLE migration_required_pages (operation_id TEXT NOT NULL, page_identity TEXT NOT NULL, surface INTEGER NOT NULL,
 table_name TEXT NOT NULL, row_count INTEGER NOT NULL, membership_hash TEXT NOT NULL,
 PRIMARY KEY(operation_id,page_identity));
''')
if mode != 'empty':
    status, reason, terminal, claimed = map(int, mode.split(','))
    terminal_value = None if terminal == 0 else '2026-10-15T00:00:00.0000000+00:00'
    for n in range(count):
        operation_id = f'00000000-0000-0000-0000-{n + 1:012x}'
        db.execute('INSERT INTO migration_operations VALUES (?,?,?,?,?,?,?,?,?,?)',
            (operation_id,'A'*64,'{}',status,reason,'2026-10-15T00:00:00Z','2026-10-15T00:00:00Z',
             terminal_value,'{}',claimed))
db.commit()
db.close()
'@

function New-JournalFixture([string] $Name, [string] $Mode = 'empty', [int] $Count = 0, [switch] $MarkerOnly, [switch] $JournalOnly) {
    $directory = Join-Path $tempRoot $Name
    [void](New-Item -ItemType Directory -Path $directory)
    if (-not $JournalOnly) {
        [IO.File]::WriteAllBytes((Join-Path $directory 'horse-identity-migration-initialized.marker'),
            [Text.Encoding]::UTF8.GetBytes('horse-identity-migration-journal-v1'))
    }
    if (-not $MarkerOnly) {
        [IO.File]::WriteAllBytes((Join-Path $directory 'horse-identity-migration-operation.lock'), [byte[]]::new(0))
        & $pythonCommand -c $seedPython (Join-Path $directory 'horse-identity-migration-journal.db') $Mode $Count
        if ($LASTEXITCODE -ne 0) { throw "Could not seed $Name journal fixture." }
    }
    return ConvertTo-BashPath $directory
}

function Test-Preflight([string] $Name, [string] $Mode, [int] $Count, [string] $Requested = '', [switch] $MarkerOnly, [switch] $JournalOnly) {
    $directory = New-JournalFixture $Name $Mode $Count -MarkerOnly:$MarkerOnly -JournalOnly:$JournalOnly
    $payload = @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
export HRP_PYTHON3=$(Quote-Bash $pythonBashPath)
result=`$(horse_id_migration_journal_preflight $(Quote-Bash $directory) $(Quote-Bash $Requested))
printf 'RESULT=%s\n' "`$result"
"@
    return Invoke-Bash $payload
}

try {
    if (-not $IsWindows) {
        $ownershipRoot = Join-Path $tempRoot 'ownership-locks'
        [void](New-Item -ItemType Directory -Path $ownershipRoot)
        $sourcePath = Join-Path $ownershipRoot 'fixture.db'
        [IO.File]::WriteAllBytes($sourcePath, [byte[]]::new(0))
        $logicalPath = '/data/fixture.db'
        $runtimeGate = Join-Path $ownershipRoot '.fixture.db.horse-identity-migration.lock'
        $controlGate = Join-Path $ownershipRoot '.fixture.db.horse-identity-migration.supervisor-control.lock'
        Write-OwnershipMarkers $sourcePath $logicalPath $runtimeGate $controlGate
        $sourceBash = ConvertTo-BashPath $sourcePath
        $journalDatabase = Join-Path $ownershipRoot 'horse-identity-migration-journal.db'
        [IO.File]::WriteAllBytes($journalDatabase, [byte[]]::new(0))
        $journalLock = Join-Path $ownershipRoot 'horse-identity-migration-operation.lock'
        [IO.File]::WriteAllBytes($journalLock, [byte[]]::new(0))
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $true)

        $runtimeBytes = [IO.File]::ReadAllBytes($runtimeGate)
        [void](Invoke-Bash "rm -- $(Quote-Bash (ConvertTo-BashPath $runtimeGate))")
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        Assert-Case (-not (Test-Path -LiteralPath $runtimeGate)) 'A missing runtime gate must not be recreated.'
        [void](Invoke-Bash ": > $(Quote-Bash (ConvertTo-BashPath $runtimeGate))")
        Assert-Case (Test-Path -LiteralPath $runtimeGate) 'The empty runtime gate fixture was not created.'
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        $emptyRuntime = Invoke-Bash "stat -c %s -- $(Quote-Bash (ConvertTo-BashPath $runtimeGate))"
        Assert-Case ($emptyRuntime.ExitCode -eq 0 -and $emptyRuntime.Output.Trim() -eq '0') 'An empty runtime gate must not be initialized.'
        [IO.File]::WriteAllBytes($runtimeGate, $runtimeBytes)
        $runtimeCorrupt = [IO.File]::ReadAllBytes($runtimeGate); $runtimeCorrupt[0] = [byte]([int]$runtimeCorrupt[0] -bxor 1)
        [IO.File]::WriteAllBytes($runtimeGate, $runtimeCorrupt)
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        [IO.File]::WriteAllBytes($runtimeGate, $runtimeBytes)
        $runtimeTooLarge = [byte[]]::new(1025); [IO.File]::WriteAllBytes($runtimeGate, $runtimeTooLarge)
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        [IO.File]::WriteAllBytes($runtimeGate, $runtimeBytes)
        $runtimeAlias = "$runtimeGate.alias"
        $runtimeAliasBash = ConvertTo-BashPath $runtimeAlias
        $runtimeGateBash = ConvertTo-BashPath $runtimeGate
        $aliasRuntime = Invoke-Bash "mv -- $(Quote-Bash $runtimeGateBash) $(Quote-Bash $runtimeAliasBash); ln -s -- $(Quote-Bash $runtimeAliasBash) $(Quote-Bash $runtimeGateBash)"
        Assert-Case ($aliasRuntime.ExitCode -eq 0) 'Could not create the runtime gate symlink fixture.'
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        [void](Invoke-Bash "rm -- $(Quote-Bash $runtimeGateBash); mv -- $(Quote-Bash $runtimeAliasBash) $(Quote-Bash $runtimeGateBash)")
        $hardRuntime = Invoke-Bash "ln -- $(Quote-Bash $runtimeGateBash) $(Quote-Bash $runtimeAliasBash)"
        Assert-Case ($hardRuntime.ExitCode -eq 0) 'Could not create the runtime gate hard-link fixture.'
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        [void](Invoke-Bash "rm -- $(Quote-Bash $runtimeAliasBash)")

        $controlBytes = [IO.File]::ReadAllBytes($controlGate)
        [void](Invoke-Bash "rm -- $(Quote-Bash (ConvertTo-BashPath $controlGate))")
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        Assert-Case (-not (Test-Path -LiteralPath $controlGate)) 'A missing supervisor control lock must not be created.'
        [void](Invoke-Bash ": > $(Quote-Bash (ConvertTo-BashPath $controlGate))")
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        $emptyControl = Invoke-Bash "stat -c %s -- $(Quote-Bash (ConvertTo-BashPath $controlGate))"
        Assert-Case ($emptyControl.ExitCode -eq 0 -and $emptyControl.Output.Trim() -eq '0') 'An empty supervisor control lock must not be initialized.'
        [IO.File]::WriteAllBytes($controlGate, $controlBytes)
        $controlCorrupt = [IO.File]::ReadAllBytes($controlGate); $controlCorrupt[0] = [byte]([int]$controlCorrupt[0] -bxor 1)
        [IO.File]::WriteAllBytes($controlGate, $controlCorrupt)
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        [IO.File]::WriteAllBytes($controlGate, $controlBytes)
        $controlTooLarge = [byte[]]::new(1025); [IO.File]::WriteAllBytes($controlGate, $controlTooLarge)
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        [IO.File]::WriteAllBytes($controlGate, $controlBytes)
        $controlAlias = "$controlGate.alias"
        $controlAliasBash = ConvertTo-BashPath $controlAlias
        $controlGateBash = ConvertTo-BashPath $controlGate
        $aliasControl = Invoke-Bash "mv -- $(Quote-Bash $controlGateBash) $(Quote-Bash $controlAliasBash); ln -s -- $(Quote-Bash $controlAliasBash) $(Quote-Bash $controlGateBash)"
        Assert-Case ($aliasControl.ExitCode -eq 0) 'Could not create the supervisor control lock symlink fixture.'
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        [void](Invoke-Bash "rm -- $(Quote-Bash $controlGateBash); mv -- $(Quote-Bash $controlAliasBash) $(Quote-Bash $controlGateBash)")
        $hardControl = Invoke-Bash "ln -- $(Quote-Bash $controlGateBash) $(Quote-Bash $controlAliasBash)"
        Assert-Case ($hardControl.ExitCode -eq 0) 'Could not create the supervisor control lock hard-link fixture.'
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        [void](Invoke-Bash "rm -- $(Quote-Bash $controlAliasBash)")
        $journalBash = ConvertTo-BashPath $journalDatabase
        [void](Invoke-Bash "rm -- $(Quote-Bash (ConvertTo-BashPath $journalLock))")
        [void](Invoke-JournalOperationLock $journalBash $false)
        Assert-Case (-not (Test-Path -LiteralPath $journalLock)) 'A missing journal operation lock must not be recreated.'
        [void](Invoke-Bash ": > $(Quote-Bash (ConvertTo-BashPath $journalLock))")
        [void](Invoke-JournalOperationLock $journalBash $true)
        [IO.File]::WriteAllText($journalLock, 'unexpected', [Text.Encoding]::ASCII)
        [void](Invoke-JournalOperationLock $journalBash $false)
        Assert-Case ((Get-Content -Raw -LiteralPath $journalLock) -eq 'unexpected') 'A nonempty journal operation lock must remain unchanged on rejection.'
        [IO.File]::WriteAllBytes($journalLock, [byte[]]::new(0))
        $journalAlias = "$journalLock.alias"
        $journalAliasBash = ConvertTo-BashPath $journalAlias
        $journalLockBash = ConvertTo-BashPath $journalLock
        $journalSymlink = Invoke-Bash "mv -- $(Quote-Bash $journalLockBash) $(Quote-Bash $journalAliasBash); ln -s -- $(Quote-Bash $journalAliasBash) $(Quote-Bash $journalLockBash)"
        Assert-Case ($journalSymlink.ExitCode -eq 0) 'Could not create the journal operation lock symlink fixture.'
        [void](Invoke-JournalOperationLock $journalBash $false)
        [void](Invoke-Bash "rm -- $(Quote-Bash $journalLockBash); mv -- $(Quote-Bash $journalAliasBash) $(Quote-Bash $journalLockBash)")
        $aliasJournal = Invoke-Bash "ln -- $(Quote-Bash $journalLockBash) $(Quote-Bash $journalAliasBash)"
        Assert-Case ($aliasJournal.ExitCode -eq 0) 'Could not create the journal operation lock alias fixture.'
        [void](Invoke-JournalOperationLock $journalBash $false)
        [void](Invoke-Bash "rm -- $(Quote-Bash $journalAliasBash)")
    Write-Output 'PASS source, control, and journal lock identity/no-reconstruction cases'
    }

    $recoveryEnvPath = Join-Path $tempRoot 'recovery.env'
    $recoveryCanary = 'RECOVERY_TEST_CANARY_NOT_FOR_OUTPUT'
    $recoveryImage = 'ghcr.io/example/horse-racing-prediction-api'
    $recoveryTag = 'sha-' + ('a' * 40)
    $originalEnv = "IMAGE_NAME=ghcr.io/old/horse-racing-prediction-api`nAPI_KEY=$recoveryCanary`nIMAGE_TAG=sha-old`nOTHER_SETTING=keep-me`n"
    [IO.File]::WriteAllText($recoveryEnvPath, $originalEnv, [Text.UTF8Encoding]::new($false))
    $envUpdate = Invoke-RecoveryEnvUpdate $recoveryEnvPath $recoveryImage $recoveryTag
    $updatedEnv = [IO.File]::ReadAllText($recoveryEnvPath)
    Assert-Case ($envUpdate.ExitCode -eq 0) 'A valid recovery image configuration should update.'
    Assert-Case ($updatedEnv -eq "IMAGE_NAME=$recoveryImage`nAPI_KEY=$recoveryCanary`nIMAGE_TAG=$recoveryTag`nOTHER_SETTING=keep-me`n") 'Recovery configuration must change only the two image coordinates.'
    Assert-Case (($envUpdate.Output -notmatch [regex]::Escape($recoveryCanary)) -and ((Invoke-Bash "stat -c %a -- $(Quote-Bash (ConvertTo-BashPath $recoveryEnvPath))").Output.Trim() -eq '600')) 'Recovery configuration must remain private and never echo unrelated secret-like values.'
    [IO.File]::AppendAllText($recoveryEnvPath, "IMAGE_TAG=duplicate`n")
    $duplicateEnvHash = (Get-FileHash -LiteralPath $recoveryEnvPath -Algorithm SHA256).Hash
    $duplicateEnv = Invoke-RecoveryEnvUpdate $recoveryEnvPath $recoveryImage $recoveryTag
    Assert-Case ($duplicateEnv.ExitCode -ne 0 -and (Get-FileHash -LiteralPath $recoveryEnvPath -Algorithm SHA256).Hash -eq $duplicateEnvHash) 'Duplicate image settings must fail without changing configuration.'
    [IO.File]::WriteAllText($recoveryEnvPath, $originalEnv, [Text.UTF8Encoding]::new($false))
    $invalidTag = Invoke-RecoveryEnvUpdate $recoveryEnvPath $recoveryImage 'latest'
    Assert-Case ($invalidTag.ExitCode -ne 0 -and [IO.File]::ReadAllText($recoveryEnvPath) -eq $originalEnv) 'A non-SHA recovery tag must fail without changing configuration.'
    Write-Output 'PASS recovery image configuration atomicity, preservation, and output safety cases'

    if (-not $IsWindows) {
        $redirectCert = ConvertTo-BashPath (Join-Path $tempRoot 'recovery-redirect.crt')
        $redirectKey = ConvertTo-BashPath (Join-Path $tempRoot 'recovery-redirect.key')
        $redirectPort = ConvertTo-BashPath (Join-Path $tempRoot 'recovery-redirect.port')
        $redirectHit = ConvertTo-BashPath (Join-Path $tempRoot 'recovery-redirect-auth-header-seen')
        $redirectRequests = ConvertTo-BashPath (Join-Path $tempRoot 'recovery-redirect-origin-requests')
        $redirectMode = ConvertTo-BashPath (Join-Path $tempRoot 'recovery-redirect-mode')
        $redirectPortQuoted = Quote-Bash $redirectPort
        $redirectCanary = 'REDIRECT_AUTH_CANARY_NEVER_PRINT'
        $redirectTest = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
export HRP_PYTHON3=$(Quote-Bash $pythonBashPath)
openssl req -x509 -newkey rsa:2048 -nodes -keyout $(Quote-Bash $redirectKey) -out $(Quote-Bash $redirectCert) -subj /CN=localhost -addext subjectAltName=DNS:localhost,IP:127.0.0.1 -days 1 >/dev/null 2>&1
python3 - $(Quote-Bash $redirectCert) $(Quote-Bash $redirectKey) $(Quote-Bash $redirectPort) $(Quote-Bash $redirectHit) $(Quote-Bash $redirectRequests) $(Quote-Bash $redirectMode) <<'PY' >/dev/null 2>&1 &
import http.server, ssl, sys
cert,key,port_file,hit_file,requests_file,mode_file=sys.argv[1:]
class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        mode=open(mode_file,encoding='ascii').read().strip()
        with open(requests_file,'a',encoding='ascii') as f:
            f.write(self.path+'|'+('key' if self.headers.get('X-Api-Key') else 'no-key')+'\n')
        if mode == 'redirect':
            if self.path == '/collect':
                if self.headers.get('X-Api-Key'):
                    with open(hit_file,'w',encoding='ascii') as f: f.write('header-forwarded')
                self.send_response(200); self.end_headers(); self.wfile.write(b'{"status":503}')
            else:
                self.send_response(302); self.send_header('Location',f'https://127.0.0.1:{self.server.server_port}/collect'); self.end_headers()
        elif self.path == '/api/v2/admin/collection/operations/runtime-status':
            self.send_response(503); self.send_header('Content-Type','application/json'); self.end_headers()
            self.wfile.write(b'{"status":503,"title":"Application startup is blocked by migration state."}')
        elif self.path == '/swagger/v1/swagger.json':
            self.send_response(200); self.send_header('Content-Type','application/json'); self.end_headers(); self.wfile.write(b'{}')
        elif self.path == '/api/v2/admin/collection/pipeline-state':
            self.send_response(200); self.send_header('Content-Type','application/json'); self.end_headers()
            self.wfile.write(b'{"pipeline":{"isPaused":true}}')
        elif self.path.startswith('/api/v2/admin/collection/tasks?'):
            self.send_response(200); self.send_header('Content-Type','application/json'); self.end_headers()
            self.wfile.write(b'{"page":{"items":[]}}')
        else:
            self.send_response(404); self.end_headers()
    def log_message(self,fmt,*args): pass
server=http.server.HTTPServer(('127.0.0.1',0),Handler)
context=ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER); context.load_cert_chain(cert,key)
server.socket=context.wrap_socket(server.socket,server_side=True)
with open(port_file,'w',encoding='ascii') as f: f.write(str(server.server_port))
server.serve_forever()
PY
server_pid=`$!
cleanup_redirect_server() { kill "`$server_pid" 2>/dev/null || true; wait "`$server_pid" 2>/dev/null || true; }
trap cleanup_redirect_server EXIT
for attempt in `$(seq 1 30); do [ -s $(Quote-Bash $redirectPort) ] && break; sleep 0.1; done
[ -s $(Quote-Bash $redirectPort) ] || exit 70
export SSL_CERT_FILE=$(Quote-Bash $redirectCert)
base="https://127.0.0.1:`$(cat $redirectPortQuoted)"
printf 'redirect\n' > $(Quote-Bash $redirectMode)
set +e
blocked_output=`$(horse_id_migration_assert_api_startup_blocked "`$base" $(Quote-Bash $redirectCanary) 2>&1); blocked_status=`$?
ready_output=`$(horse_id_migration_assert_recovery_api_ready "`$base" $(Quote-Bash $redirectCanary) 2>&1); ready_status=`$?
set -e
test "`$blocked_status" -ne 0 && test "`$ready_status" -ne 0
test ! -e $(Quote-Bash $redirectHit)
test "`$(wc -l < $(Quote-Bash $redirectRequests))" -eq 2
grep -Fx '/api/v2/admin/collection/operations/runtime-status|key' $(Quote-Bash $redirectRequests) >/dev/null
grep -Fx '/api/v2/admin/collection/pipeline-state|key' $(Quote-Bash $redirectRequests) >/dev/null
case "`$blocked_output`$ready_output" in *$(Quote-Bash $redirectCanary)*|*127.0.0.1*|*redirect*) exit 71;; esac
printf 'success\n' > $(Quote-Bash $redirectMode)
blocked_success=`$(horse_id_migration_assert_api_startup_blocked "`$base" $(Quote-Bash $redirectCanary) 2>&1)
printf 'PASS code=RECOVERY_API_BLOCKED\n'
swagger_success=`$(horse_id_migration_wait_recovery_swagger "`$base")
ready_success=`$(horse_id_migration_assert_recovery_api_ready "`$base" $(Quote-Bash $redirectCanary) 2>&1)
test "`$(wc -l < $(Quote-Bash $redirectRequests))" -eq 6
grep -Fx '/api/v2/admin/collection/operations/runtime-status|key' $(Quote-Bash $redirectRequests) >/dev/null
grep -Fx '/swagger/v1/swagger.json|no-key' $(Quote-Bash $redirectRequests) >/dev/null
grep -Fx '/api/v2/admin/collection/pipeline-state|key' $(Quote-Bash $redirectRequests) >/dev/null
grep -Fx '/api/v2/admin/collection/tasks?statuses=Running&pageSize=1|key' $(Quote-Bash $redirectRequests) >/dev/null
case "`$blocked_success` `$swagger_success` `$ready_success" in *$(Quote-Bash $redirectCanary)*|*127.0.0.1*) exit 72;; esac
test ! -e $(Quote-Bash $redirectHit)
printf '%s\n%s\n%s\n' "`$blocked_success" "`$swagger_success" "`$ready_success"
"@ 20000
        Assert-Case ($redirectTest.ExitCode -eq 0) "Authenticated recovery helpers must reject redirects without forwarding credentials or logging test values. $($redirectTest.Output)"
        Assert-Case (($redirectTest.Output -match 'PASS code=RECOVERY_API_BLOCKED') -and ($redirectTest.Output -match 'PASS code=RECOVERY_SWAGGER_READY') -and ($redirectTest.Output -match 'PASS code=RECOVERY_API_READY_PAUSED')) "Recovery helper positive controls must reach the trusted TLS origin and validate blocked/ready responses. $($redirectTest.Output)"
        Write-Output 'PASS trusted TLS positive controls, origin request counts, redirect refusal, and secret-canary cases'
    }

    $safeRecoveryState = New-RecoveryStateFixture 'recovery-state-safe'
    [void](Test-RecoveryState $safeRecoveryState $true)
    foreach ($case in @(
        @{ Name = 'recovery-state-unpaused'; Control = 'present'; Pause = '0'; Task = 'empty'; Lease = 'empty'; Version = '25' },
        @{ Name = 'recovery-state-no-pipeline'; Control = 'missing'; Pause = '1'; Task = 'empty'; Lease = 'empty'; Version = '25' },
        @{ Name = 'recovery-state-running-task'; Control = 'present'; Pause = '1'; Task = 'Running'; Lease = 'empty'; Version = '25' },
        @{ Name = 'recovery-state-running-lease'; Control = 'present'; Pause = '1'; Task = 'empty'; Lease = 'Running'; Version = '25' },
        @{ Name = 'recovery-state-pending-lease'; Control = 'present'; Pause = '1'; Task = 'empty'; Lease = 'StartPending'; Version = '25' },
        @{ Name = 'recovery-state-unknown-task'; Control = 'present'; Pause = '1'; Task = 'Unexpected'; Lease = 'empty'; Version = '25' },
        @{ Name = 'recovery-state-unknown-lease'; Control = 'present'; Pause = '1'; Task = 'empty'; Lease = 'Unexpected'; Version = '25' },
        @{ Name = 'recovery-state-schema-version'; Control = 'present'; Pause = '1'; Task = 'empty'; Lease = 'empty'; Version = '24' }
    )) {
        $path = New-RecoveryStateFixture $case.Name $case.Version $case.Control $case.Pause $case.Task $case.Lease
        [void](Test-RecoveryState $path $false)
    }

    $guid = '00000000-0000-0000-0000-000000000001'
    $fresh = Test-Preflight 'fresh' 'empty' 0
    Assert-Case ($fresh.ExitCode -eq 0 -and $fresh.Output -match 'RESULT=\r?\n') "Fresh install must be allowed without an operation GUID. Exit=$($fresh.ExitCode); output=$($fresh.Output)"
    $staged = Test-Preflight 'staged' '0,0,0,0' 1 $guid
    Assert-Case ($staged.ExitCode -eq 0) 'Exact unclaimed staged operation must be permitted in migration mode.'
    $guidValidation = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
horse_id_migration_validate_guid $(Quote-Bash $guid)
if horse_id_migration_validate_guid 00000000-0000-0000-0000-000000000000; then exit 80; fi
if horse_id_migration_validate_guid not-a-guid; then exit 81; fi
"@
    Assert-Case ($guidValidation.ExitCode -eq 0) 'GUID.Empty and malformed values must be rejected at the helper boundary.'
    $stagedBlocked = Test-Preflight 'staged-normal' '0,0,0,0' 1
    Assert-Case ($stagedBlocked.ExitCode -ne 0) 'Normal deploy must block staged operations.'
    $wrongGuid = Test-Preflight 'wrong-guid' '0,0,0,0' 1 '00000000-0000-0000-0000-000000000002'
    Assert-Case ($wrongGuid.ExitCode -ne 0) 'Migration mode must reject a different GUID.'
    $mixedCase = New-JournalFixture 'mixed-case-ids'
    $insertCaseRows = @'
import sqlite3,sys
db=sqlite3.connect(sys.argv[1])
base='00000000-0000-0000-0000-0000000000ab'
values=(2,0,'2026-10-15T00:00:00Z','2026-10-15T00:00:00Z','2026-10-15T00:00:00Z','{}',1)
for operation_id in (base.upper(),base):
 db.execute('INSERT INTO migration_operations(operation_id,request_hash,request_json,status,reason,created_utc,updated_utc,terminal_utc,results_json,workflow_claimed) VALUES(?,?,?, ?,?,?, ?,?,?,?)',(operation_id,'A'*64,'{}',*values))
db.commit()
'@
    & $pythonCommand -c $insertCaseRows (Join-Path (Join-Path $tempRoot 'mixed-case-ids') 'horse-identity-migration-journal.db')
    if ($LASTEXITCODE -ne 0) { throw 'Could not seed mixed-case duplicate operation IDs.' }
    $mixedCasePreflight = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
export HRP_PYTHON3=$(Quote-Bash $pythonBashPath)
horse_id_migration_journal_preflight $(Quote-Bash $mixedCase) ''
"@
    Assert-Case ($mixedCasePreflight.ExitCode -ne 0) 'Noncanonical uppercase/case-normalized duplicate operation IDs must block preflight.'
    $multipleStaged = Test-Preflight 'multiple-staged' '0,0,0,0' 2 $guid
    Assert-Case ($multipleStaged.ExitCode -ne 0) 'Migration mode must block more than the exact staged operation.'
    foreach ($case in @(
        @{ Name = 'running'; Mode = '1,0,0,1' },
        @{ Name = 'interrupted'; Mode = '5,6,0,1' },
        @{ Name = 'partial'; Mode = '6,6,0,1' },
        @{ Name = 'invalid-enum'; Mode = '9,0,0,1' },
        @{ Name = 'succeeded-bad-reason'; Mode = '2,4,1,1' },
        @{ Name = 'succeeded-no-claim'; Mode = '2,0,1,0' },
        @{ Name = 'staged-claimed'; Mode = '0,0,0,1' },
        @{ Name = 'running-terminal'; Mode = '1,0,1,1' }
    )) {
        $result = Test-Preflight $case.Name $case.Mode 1
        Assert-Case ($result.ExitCode -ne 0) "Guard accepted corrupt or active state $($case.Name)."
    }
    $success = Test-Preflight 'succeeded' '2,0,1,1' 1
    Assert-Case ($success.ExitCode -eq 0 -and $success.Output -match [regex]::Escape("RESULT=$guid")) 'Succeeded operation GUID must be returned for canonical fixed-command validation.'
    $markerOnly = Test-Preflight 'marker-only' 'empty' 0 -MarkerOnly
    Assert-Case ($markerOnly.ExitCode -ne 0) 'Missing initialized journal must fail closed.'
    $journalOnly = Test-Preflight 'journal-only' 'empty' 0 -JournalOnly
    Assert-Case ($journalOnly.ExitCode -ne 0) 'Journal without its initialization marker must fail closed.'
    $tooManyRows = Test-Preflight 'row-cap' '0,0,0,0' 4097 $guid
    Assert-Case ($tooManyRows.ExitCode -ne 0) 'Operation row cap must fail closed.'
    $largeMarker = Join-Path $tempRoot 'large-marker'
    [void](New-Item -ItemType Directory -Path $largeMarker)
    [IO.File]::WriteAllBytes((Join-Path $largeMarker 'horse-identity-migration-initialized.marker'), [byte[]]::new(65))
    [IO.File]::WriteAllBytes((Join-Path $largeMarker 'horse-identity-migration-operation.lock'), [byte[]]::new(0))
    & $pythonCommand -c $seedPython (Join-Path $largeMarker 'horse-identity-migration-journal.db') 'empty' 0
    if ($LASTEXITCODE -ne 0) { throw 'Could not seed large marker fixture.' }
    $large = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
export HRP_PYTHON3=$(Quote-Bash $pythonBashPath)
horse_id_migration_journal_preflight $(Quote-Bash (ConvertTo-BashPath $largeMarker)) ''
"@
    Assert-Case ($large.ExitCode -ne 0) 'Marker over the fixed byte cap must fail closed.'

    $data = Join-Path $tempRoot 'backup-data'
    $backup = Join-Path $tempRoot 'backup-out'
    [void](New-Item -ItemType Directory -Path $data)
    $seedBackupStores = @'
import sqlite3,sys
from pathlib import Path
root=Path(sys.argv[1])
for name in ('eventstore','collection-platform','prediction-executions'):
    db=sqlite3.connect(root/(name+'.db'))
    db.execute('CREATE TABLE fixture (value TEXT NOT NULL)')
    db.execute('INSERT INTO fixture VALUES (?)',(name,))
    db.commit(); db.close()
'@
    & $pythonCommand -c $seedBackupStores (ConvertTo-BashPath $data)
    if ($LASTEXITCODE -ne 0) { throw 'Could not seed real SQLite backup fixtures.' }
    [IO.File]::WriteAllBytes((Join-Path $data 'eventstore.db-wal'), [byte[]]::new(0))
    [IO.File]::WriteAllText((Join-Path $data 'eventstore.db-shm'), 'shm')
    [IO.File]::WriteAllText((Join-Path $data 'collection-platform.db-journal'), '')
    $copy = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
horse_id_migration_copy_store_set $(Quote-Bash (ConvertTo-BashPath $data)) $(Quote-Bash (ConvertTo-BashPath $backup)) yes eventstore.db collection-platform.db prediction-executions.db
horse_id_migration_assert_quiescent_stores $(Quote-Bash (ConvertTo-BashPath $data)) eventstore.db collection-platform.db prediction-executions.db
"@
    Assert-Case ($copy.ExitCode -eq 0) "Backup with zero WAL and present sidecars should succeed. Exit=$($copy.ExitCode); output=$($copy.Output)"
    foreach ($file in @('eventstore.db','eventstore.db-wal','eventstore.db-shm','collection-platform.db','collection-platform.db-journal','prediction-executions.db')) {
        Assert-Case (Test-Path -LiteralPath (Join-Path $backup $file)) "Coherent backup omitted $file."
    }
    $manifestCheck = & $pythonCommand -c "import hashlib,pathlib,sys; p=pathlib.Path(sys.argv[1]); rows=(p/'SHA256SUMS').read_text().splitlines(); assert rows; [(lambda h,n: (_ for _ in ()).throw(AssertionError(n)) if hashlib.sha256((p/n).read_bytes()).hexdigest()!=h else None)(line.split('  ',1)[0],line.split('  ',1)[1]) for line in rows]" $backup 2>&1
    Assert-Case ($LASTEXITCODE -eq 0) 'Backup hash manifest must match every copied file.'
    $backupSqliteCheckScript = @'
import sqlite3,sys
from pathlib import Path
root=Path(sys.argv[1])
for name in ('eventstore.db','collection-platform.db','prediction-executions.db'):
    db=sqlite3.connect(root/name)
    assert db.execute('PRAGMA integrity_check').fetchall()==[('ok',)]
    db.close()
'@
    $backupSqliteCheck = & $pythonCommand -c $backupSqliteCheckScript $backup 2>&1
    Assert-Case ($LASTEXITCODE -eq 0) 'Backup must preserve readable SQLite content, not merely copy arbitrary bytes.'

    $badWalData = Join-Path $tempRoot 'bad-wal-data'
    $badWalBackup = Join-Path $tempRoot 'bad-wal-backup'
    [void](New-Item -ItemType Directory -Path $badWalData)
    & $pythonCommand -c "import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute('CREATE TABLE fixture (value TEXT)'); c.commit(); c.close()" (Join-Path $badWalData 'eventstore.db')
    if ($LASTEXITCODE -ne 0) { throw 'Could not seed bad-WAL SQLite fixture.' }
    [IO.File]::WriteAllText((Join-Path $badWalData 'eventstore.db-wal'), 'pending')
    $badWal = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
horse_id_migration_copy_store_set $(Quote-Bash (ConvertTo-BashPath $badWalData)) $(Quote-Bash (ConvertTo-BashPath $badWalBackup)) yes eventstore.db
"@
    Assert-Case ($badWal.ExitCode -ne 0) 'Non-empty WAL must block backup/apply.'
    $wouldStartApi = Join-Path $tempRoot 'bad-wal-api-started'
    $badWalOrder = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
horse_id_migration_copy_store_set $(Quote-Bash (ConvertTo-BashPath $badWalData)) $(Quote-Bash (ConvertTo-BashPath (Join-Path $tempRoot 'bad-wal-order-backup'))) yes eventstore.db
: > $(Quote-Bash (ConvertTo-BashPath $wouldStartApi))
"@
    Assert-Case ($badWalOrder.ExitCode -ne 0 -and -not (Test-Path -LiteralPath $wouldStartApi)) 'A failed SQLite backup must prevent the subsequent API-start boundary.'

    $stubBin = Join-Path $tempRoot 'bin'
    [void](New-Item -ItemType Directory -Path $stubBin)
    $cleanupFile = Join-Path $tempRoot 'container-cleanup.log'
    $childFile = Join-Path $tempRoot 'stalled-child.pid'
    $dockerStub = @'
#!/usr/bin/env bash
set -eu
case "$1" in
  image) printf '%s\n' "${TEST_DOCKER_LABEL-v1}" ;;
  compose)
    if [[ " $* " == *" config --format json "* ]]; then cat "$TEST_COMPOSE_CONFIG"; else exit 0; fi ;;
  wait)
    if [ "${TEST_DOCKER_MODE:-success}" = stall ]; then
      sleep 30 &
      echo $! > "$TEST_CHILD_FILE"
      wait
    else
      echo 0
    fi ;;
  inspect) printf '%s\n' "${TEST_DOCKER_EXIT:-0}" ;;
  rm) echo cleaned >> "$TEST_CLEANUP_FILE" ;;
  *) exit 98 ;;
esac
'@
    [IO.File]::WriteAllText((Join-Path $stubBin 'docker'), $dockerStub)
    $stubBinBash = ConvertTo-BashPath $stubBin
    $chmodStub = Invoke-Bash "chmod +x $(Quote-Bash (ConvertTo-BashPath (Join-Path $stubBin 'docker')))"
    Assert-Case ($chmodStub.ExitCode -eq 0) 'Could not make the isolated Docker stub executable.'
    $cleanupBash = ConvertTo-BashPath $cleanupFile
    $childBash = ConvertTo-BashPath $childFile
    $goodLabel = Invoke-Bash @"
set -euo pipefail
export PATH=$(Quote-Bash $stubBinBash):"`$PATH"
export TEST_DOCKER_LABEL=v1
. $(Quote-Bash $helperBashPath)
horse_id_migration_check_image_capability example/api:sha-test
"@
    Assert-Case ($goodLabel.ExitCode -eq 0) 'Expected capability label should pass without starting a container.'
    $missingLabel = Invoke-Bash @"
set -euo pipefail
export PATH=$(Quote-Bash $stubBinBash):"`$PATH"
export TEST_DOCKER_LABEL=
. $(Quote-Bash $helperBashPath)
horse_id_migration_check_image_capability example/api:old
"@
    Assert-Case ($missingLabel.ExitCode -ne 0) 'Missing capability label must block before compose-up.'

    $configuredData = Join-Path $tempRoot 'rendered-config-data'
    foreach ($directory in @('race-store','collection-state','prediction-state')) {
        [void](New-Item -ItemType Directory -Path (Join-Path $configuredData $directory) -Force)
    }
    foreach ($database in @('race-store/event-store.db','collection-state/current.db','prediction-state/exec.db','horse-identity-migration-journal.db','local-collection-queue.db')) {
        $databasePath = Join-Path $configuredData $database
        [void](New-Item -ItemType File -Path $databasePath -Force)
    }
    $seedConfiguredStores = @'
import sqlite3,sys
for path in sys.argv[1:]:
 db=sqlite3.connect(path)
 db.execute('CREATE TABLE IF NOT EXISTS integrity_fixture (value TEXT NOT NULL)')
 db.execute('INSERT INTO integrity_fixture(value) VALUES (?)',('fixture',))
 db.commit(); db.close()
'@
    $configuredPaths = @(
        (Join-Path $configuredData 'race-store/event-store.db'),
        (Join-Path $configuredData 'collection-state/current.db'),
        (Join-Path $configuredData 'prediction-state/exec.db'),
        (Join-Path $configuredData 'horse-identity-migration-journal.db'),
        (Join-Path $configuredData 'local-collection-queue.db')
    )
    & $pythonCommand -c $seedConfiguredStores @configuredPaths
    if ($LASTEXITCODE -ne 0) { throw 'Could not seed configured application SQLite fixtures.' }
    $configuredDataBash = ConvertTo-BashPath $configuredData
    $composeFixturePath = Join-Path $tempRoot 'rendered-compose.json'
    $composeFixture = @{
        services = @{
            api = @{
                volumes = @(@{ type = 'bind'; source = (Resolve-Path $configuredData).Path; target = '/data' })
                environment = @{
                    ConnectionStrings__EventStore = 'Data Source=/data/race-store/event-store.db'
                    CollectionPlatform__StateDirectory = '/data/collection-state'
                    CollectionPlatform__DatabaseFileName = 'current.db'
                    PredictionScheduling__StateDirectory = '/data/prediction-state'
                    PredictionScheduling__StoreFileName = 'exec.db'
                }
            }
        }
    } | ConvertTo-Json -Depth 8 -Compress
    [IO.File]::WriteAllText($composeFixturePath, $composeFixture)
    $composeFixtureBash = ConvertTo-BashPath $composeFixturePath
    $renderedStores = Invoke-Bash @"
set -euo pipefail
export PATH=$(Quote-Bash $stubBinBash):"`$PATH"
export TEST_COMPOSE_CONFIG=$(Quote-Bash $composeFixtureBash)
export HRP_PYTHON3=$(Quote-Bash $pythonBashPath)
. $(Quote-Bash $helperBashPath)
stores=`$(horse_id_migration_configured_store_paths $(Quote-Bash $configuredDataBash))
printf 'STORE_PATHS=%s\n' "`$stores"
mapfile -t store_array <<< "`$stores"
for index in "`${!store_array[@]}"; do store_array[`$index]=`${store_array[`$index]%`$'\r'}; done
horse_id_migration_copy_store_set $(Quote-Bash $configuredDataBash) $(Quote-Bash (ConvertTo-BashPath (Join-Path $tempRoot 'rendered-backup'))) yes "`${store_array[@]}"
"@
    Assert-Case ($renderedStores.ExitCode -eq 0) "Rendered Compose database paths must resolve and copy. $($renderedStores.Output)"
    Assert-Case ($renderedStores.Output -match 'STORE_PATHS=race-store/event-store\.db\r?\ncollection-state/current\.db\r?\nprediction-state/exec\.db\r?\nhorse-identity-migration-journal\.db') 'Store paths must come from the rendered Compose environment, not hard-coded basenames.'
    $integrityOk = Invoke-Bash @"
set -euo pipefail
export PATH=$(Quote-Bash $stubBinBash):"`$PATH"
export TEST_COMPOSE_CONFIG=$(Quote-Bash $composeFixtureBash) HRP_PYTHON3=$(Quote-Bash $pythonBashPath)
. $(Quote-Bash $helperBashPath)
horse_id_migration_check_configured_store_integrity $(Quote-Bash $configuredDataBash)
"@
    $integrityOkRows = [regex]::Matches($integrityOk.Output, '(?m)^ok\r?$').Count
    Assert-Case ($integrityOk.ExitCode -eq 0 -and $integrityOkRows -eq 3 -and $integrityOk.Output -match 'PASS code=INTEGRITY_CHECK checked=3') "SQS integrity must emit exactly one ok per configured application DB and exclude the journal/optional local queue. $($integrityOk.Output)"

    $corruptStorePath = Join-Path $configuredData 'race-store/event-store.db'
    [IO.File]::WriteAllText($corruptStorePath, 'corrupt-private-fixture-marker')
    $integrityCorrupt = Invoke-Bash @"
set -euo pipefail
export PATH=$(Quote-Bash $stubBinBash):"`$PATH"
export TEST_COMPOSE_CONFIG=$(Quote-Bash $composeFixtureBash) HRP_PYTHON3=$(Quote-Bash $pythonBashPath)
. $(Quote-Bash $helperBashPath)
if horse_id_migration_check_configured_store_integrity $(Quote-Bash $configuredDataBash); then exit 80; fi
"@
    Assert-Case ($integrityCorrupt.ExitCode -eq 0 -and $integrityCorrupt.Output -match 'FAIL code=INTEGRITY_(RESULT|QUERY) checked=0') "Corrupt SQLite must fail with a fixed result code and count. $($integrityCorrupt.Output)"
    Assert-Case ($integrityCorrupt.Output -notmatch [regex]::Escape($configuredData) -and $integrityCorrupt.Output -notmatch 'corrupt-private-fixture-marker|Traceback|sqlite3\.') 'Integrity failure output must not expose raw paths, SQLite details, or fixture data.'
    Remove-Item -LiteralPath $corruptStorePath
    & $pythonCommand -c $seedConfiguredStores @configuredPaths
    if ($LASTEXITCODE -ne 0) { throw 'Could not restore the valid configured SQLite fixtures.' }

    $missingStorePath = Join-Path $configuredData 'prediction-state/exec.db'
    Remove-Item -LiteralPath $missingStorePath
    $missingIntegrityStore = Invoke-Bash @"
set -euo pipefail
export PATH=$(Quote-Bash $stubBinBash):"`$PATH"
export TEST_COMPOSE_CONFIG=$(Quote-Bash $composeFixtureBash) HRP_PYTHON3=$(Quote-Bash $pythonBashPath)
. $(Quote-Bash $helperBashPath)
if horse_id_migration_check_configured_store_integrity $(Quote-Bash $configuredDataBash); then exit 80; fi
"@
    Assert-Case ($missingIntegrityStore.ExitCode -eq 0 -and $missingIntegrityStore.Output -match 'FAIL code=STORE_SET checked=0') 'A missing required configured store must fail before SQLite inspection.'
    & $pythonCommand -c $seedConfiguredStores @configuredPaths
    if ($LASTEXITCODE -ne 0) { throw 'Could not restore the missing configured SQLite fixture.' }

    $manifestJson = & $pythonCommand -c "import json,pathlib,sys; print(json.dumps([line.split('  ',1)[1] for line in pathlib.Path(sys.argv[1],'SHA256SUMS').read_text().splitlines()]))" (Join-Path $tempRoot 'rendered-backup')
    if ($LASTEXITCODE -ne 0) { throw 'Could not read rendered-config backup membership.' }
    $manifestNames = @($manifestJson | ConvertFrom-Json)
    $expectedManifestNames = @('collection-state/current.db','horse-identity-migration-journal.db','prediction-state/exec.db','race-store/event-store.db')
    $actualSortedNames = @($manifestNames | Sort-Object)
    $expectedSortedNames = @($expectedManifestNames | Sort-Object)
    Assert-Case (($actualSortedNames -join "`n") -eq ($expectedSortedNames -join "`n")) "SQS backup manifest must exactly match the independently declared rendered-config database paths; actual=$($manifestNames -join ',')"

    $localQueuePath = Join-Path $configuredData 'localq/queue.db'
    [void](New-Item -ItemType Directory -Path (Split-Path $localQueuePath) -Force)
    & $pythonCommand -c $seedConfiguredStores $localQueuePath
    if ($LASTEXITCODE -ne 0) { throw 'Could not seed the configured Local queue SQLite fixture.' }
    $localCompose = $composeFixture -replace '"PredictionScheduling__StoreFileName":"exec.db"', '"PredictionScheduling__StoreFileName":"exec.db","CollectionQueue__Provider":"Local","CollectionQueue__LocalDatabasePath":"/data/localq/queue.db"'
    $localComposePath = Join-Path $tempRoot 'rendered-local-compose.json'
    [IO.File]::WriteAllText($localComposePath, $localCompose)
    $localComposeBash = ConvertTo-BashPath $localComposePath
    $localQueue = Invoke-Bash @"
set -euo pipefail
export PATH=$(Quote-Bash $stubBinBash):"`$PATH"
export TEST_COMPOSE_CONFIG=$(Quote-Bash $localComposeBash) HRP_PYTHON3=$(Quote-Bash $pythonBashPath)
. $(Quote-Bash $helperBashPath)
horse_id_migration_configured_store_paths $(Quote-Bash $configuredDataBash)
"@
    $expectedLocalStores = @('race-store/event-store.db','collection-state/current.db','prediction-state/exec.db','localq/queue.db','horse-identity-migration-journal.db') | Sort-Object
    $actualLocalStores = @($localQueue.Output -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object)
    Assert-Case ($localQueue.ExitCode -eq 0 -and ($actualLocalStores -join "`n") -eq ($expectedLocalStores -join "`n")) 'Local provider must resolve exactly the three app stores, configured Local queue, and journal.'
    $localIntegrity = Invoke-Bash @"
set -euo pipefail
export PATH=$(Quote-Bash $stubBinBash):"`$PATH"
export TEST_COMPOSE_CONFIG=$(Quote-Bash $localComposeBash) HRP_PYTHON3=$(Quote-Bash $pythonBashPath)
. $(Quote-Bash $helperBashPath)
horse_id_migration_check_configured_store_integrity $(Quote-Bash $configuredDataBash)
"@
    $localIntegrityRows = [regex]::Matches($localIntegrity.Output, '(?m)^ok\r?$').Count
    Assert-Case ($localIntegrity.ExitCode -eq 0 -and $localIntegrityRows -eq 4 -and $localIntegrity.Output -match 'PASS code=INTEGRITY_CHECK checked=4') 'Local provider integrity must add exactly the configured persistent queue database.'
    Remove-Item -LiteralPath $localQueuePath
    $missingLocalQueue = Invoke-Bash @"
set -euo pipefail
export PATH=$(Quote-Bash $stubBinBash):"`$PATH"
export TEST_COMPOSE_CONFIG=$(Quote-Bash $localComposeBash) HRP_PYTHON3=$(Quote-Bash $pythonBashPath)
. $(Quote-Bash $helperBashPath)
horse_id_migration_configured_store_paths $(Quote-Bash $configuredDataBash)
"@
    Assert-Case ($missingLocalQueue.ExitCode -ne 0) 'Missing configured local queue must block migration backup.'

    $stallPythonPath = Join-Path $tempRoot 'python-integrity-stall'
    $stallParentFile = Join-Path $tempRoot 'integrity-stall-parent.pid'
    $stallChildFile = Join-Path $tempRoot 'integrity-stall-child.pid'
    $stallPython = @'
#!/usr/bin/env bash
set -eu
if [[ "${1-}" == "-c" ]]; then exec "$TEST_REAL_PYTHON" "$@"; fi
if [[ "${1-}" == "-" ]]; then
  echo "$$" > "$TEST_INTEGRITY_PARENT_FILE"
  sleep 120 &
  echo "$!" > "$TEST_INTEGRITY_CHILD_FILE"
  wait
fi
exit 98
'@
    [IO.File]::WriteAllText($stallPythonPath, $stallPython)
    $stallPythonBash = ConvertTo-BashPath $stallPythonPath
    $stallParentBash = ConvertTo-BashPath $stallParentFile
    $stallChildBash = ConvertTo-BashPath $stallChildFile
    $stallChmod = Invoke-Bash "chmod +x $(Quote-Bash $stallPythonBash)"
    Assert-Case ($stallChmod.ExitCode -eq 0) 'Could not make the integrity stall child executable.'
    $integrityStall = Invoke-Bash @"
set -euo pipefail
export PATH=$(Quote-Bash $stubBinBash):"`$PATH"
export TEST_COMPOSE_CONFIG=$(Quote-Bash $composeFixtureBash) TEST_REAL_PYTHON=$(Quote-Bash $pythonBashPath)
export TEST_INTEGRITY_PARENT_FILE=$(Quote-Bash $stallParentBash) TEST_INTEGRITY_CHILD_FILE=$(Quote-Bash $stallChildBash)
export HRP_PYTHON3=$(Quote-Bash $stallPythonBash)
. $(Quote-Bash $helperBashPath)
if horse_id_migration_check_configured_store_integrity $(Quote-Bash $configuredDataBash); then exit 80; fi
"@ 110000
    Assert-Case ($integrityStall.ExitCode -eq 0 -and $integrityStall.Output -match 'FAIL code=INTEGRITY_DEADLINE checked=0') "Stalled integrity child must hit the fixed outer deadline. $($integrityStall.Output)"
    Assert-Case ($integrityStall.Output -notmatch [regex]::Escape($configuredData) -and $integrityStall.Output -notmatch 'Traceback|sqlite3\.') 'Deadline output must not expose raw paths or child errors.'
    if (Test-Path -LiteralPath $stallChildFile) {
        $stallChildPid = Get-Content -LiteralPath $stallChildFile -Raw
        if (-not $IsWindows -and $stallChildPid -match '^\d+$') {
            Start-Sleep -Milliseconds 300
            $stallChildState = & /bin/bash -c "ps -o stat= -p $stallChildPid 2>/dev/null || true"
            Assert-Case ([string]::IsNullOrWhiteSpace(($stallChildState -join '').Trim()) -or (($stallChildState -join '').Trim().StartsWith('Z'))) 'Timed-out integrity child process tree must be terminated.'
        }
    }

    $timeoutTest = Invoke-Bash @"
set -euo pipefail
export PATH=$(Quote-Bash $stubBinBash):"`$PATH"
export TEST_DOCKER_MODE=stall TEST_CLEANUP_FILE=$(Quote-Bash $cleanupBash) TEST_CHILD_FILE=$(Quote-Bash $childBash)
export HRP_MIGRATION_CLI_TIMEOUT_SECONDS=2 HRP_MIGRATION_CHILD_GRACE_SECONDS=1
. $(Quote-Bash $helperBashPath)
if horse_id_migration_run_fixed_command $(Quote-Bash $guid) example/api:sha-test /dev/null; then exit 80; fi
"@ 10000
    Assert-Case ($timeoutTest.ExitCode -eq 0) 'Stalled child must time out within its bound.'
    Assert-Case (Test-Path -LiteralPath $cleanupFile) 'Timed-out child container must be removed in cleanup.'
    $childPid = Get-Content -LiteralPath $childFile -Raw
    if (-not $IsWindows -and $childPid -match '^\d+$') {
        Start-Sleep -Milliseconds 300
        $state = & /bin/bash -c "ps -o stat= -p $childPid 2>/dev/null || true"
        Assert-Case ([string]::IsNullOrWhiteSpace(($state -join '').Trim()) -or (($state -join '').Trim().StartsWith('Z'))) 'Stalled child process tree must be terminated.'
    }

    $remote = [regex]::Match($workflow, '(?s)- name: Restart remote stack.*?          script: \|\r?\n(?<body>.*?)\r?\n      - name: Verify deployment health')
    Assert-Case $remote.Success 'Remote deploy script was not found.'
    $remoteScript = ($remote.Groups['body'].Value -split '\r?\n' | ForEach-Object { $_ -replace '^            ', '' }) -join "`n"
    $ordered = @(
        '. "$APP_DIRECTORY/horse-id-migration-deploy.sh"',
        'horse_id_migration_require_tools',
        'docker compose --env-file .env pull',
        'horse_id_migration_check_image_capability',
        'horse_id_migration_assert_api_paused',
        'docker compose --env-file .env stop api',
        'horse_id_migration_journal_preflight',
        'horse_id_migration_copy_store_set',
        'horse_id_migration_run_fixed_command "$HORSE_ID_MIGRATION_OPERATION_ID"',
        'docker compose --env-file .env up -d --force-recreate --remove-orphans'
    )
    $previous = -1
    foreach ($needle in $ordered) {
        $position = $remoteScript.IndexOf($needle, [StringComparison]::Ordinal)
        Assert-Case ($position -gt $previous) "Remote deploy order missing/out of order: $needle"
        $previous = $position
    }
    Assert-Case ($helper -match 'paths\.append\("horse-identity-migration-journal\.db"\)') 'Rendered store set must include the fixed migration journal.'
    $sourceOwnershipHelper = [regex]::Match($helper, '(?s)horse_id_migration_acquire_source_ownership\(\) \{(?<body>.*?)\r?\n\}')
    $journalLockHelper = [regex]::Match($helper, '(?s)horse_id_migration_acquire_journal_operation_lock\(\) \{(?<body>.*?)\r?\n\}')
    Assert-Case ($sourceOwnershipHelper.Success -and $sourceOwnershipHelper.Groups['body'].Value.IndexOf('stat -c %s', [StringComparison]::Ordinal) -lt $sourceOwnershipHelper.Groups['body'].Value.IndexOf('actual=$(<"$gate")', [StringComparison]::Ordinal)) 'Runtime/control gate exact-size metadata must be checked before shell content reads.'
    Assert-Case ($journalLockHelper.Success -and $journalLockHelper.Groups['body'].Value.IndexOf('stat -c %s', [StringComparison]::Ordinal) -lt $journalLockHelper.Groups['body'].Value.IndexOf('exec {fd}<', [StringComparison]::Ordinal)) 'Journal operation lock exact-size metadata must be checked before opening it.'
    Assert-Case ($remoteScript -match 'horse_id_migration_configured_store_paths') 'Backup members must be derived from the rendered Compose configuration.'
    Assert-Case ($remoteScript -match 'if \[ -z "\$HORSE_ID_MIGRATION_OPERATION_ID" \].*docker image prune') 'Migration failure path must not prune/rollback to an old image.'
    Assert-Case ($workflow -match 'horse_id_migration_operation_id:[\s\S]*required: false[\s\S]*type: string') 'Workflow dispatch migration GUID must remain optional.'
    Assert-Case ($workflow -match 'pause_confirmed_at=\$\(date \+%s\)[\s\S]*quiescent_at=\$\(\(pause_confirmed_at \+ 900\)\)') 'Old Lambda quiescence must be measured from persisted pause confirmation.'
    $initialPauseGuard = $workflow.IndexOf('if [ -n "$HORSE_ID_MIGRATION_OPERATION_ID" ] && [ "$was_paused" != "true" ]; then', [StringComparison]::Ordinal)
    $firstPausePut = $workflow.IndexOf("--data '{`"pipeline`":{`"paused`":true,`"reason`":`"deployment version transition`"}}'", $initialPauseGuard, [StringComparison]::Ordinal)
    Assert-Case ($initialPauseGuard -ge 0 -and $firstPausePut -gt $initialPauseGuard) 'Migration mode must reject an unpaused collection pipeline before any pause PUT.'
    $quiescenceBoundary = $workflow.IndexOf('quiescent_at=$((pause_confirmed_at + 900))', [StringComparison]::Ordinal)
    $finalPauseRead = -1
    if ($quiescenceBoundary -ge 0) { $finalPauseRead = $workflow.IndexOf('final_pause_state=', $quiescenceBoundary, [StringComparison]::Ordinal) }
    $finalPauseCheck = -1
    if ($finalPauseRead -ge 0) { $finalPauseCheck = $workflow.IndexOf('.pipeline.isPaused == true', $finalPauseRead, [StringComparison]::Ordinal) }
    $finalTaskRead = -1
    if ($finalPauseCheck -ge 0) { $finalTaskRead = $workflow.IndexOf('final_running=', $finalPauseCheck, [StringComparison]::Ordinal) }
    Assert-Case ($quiescenceBoundary -ge 0 -and $finalPauseRead -gt $quiescenceBoundary -and $finalPauseCheck -gt $finalPauseRead -and $finalTaskRead -gt $finalPauseCheck) 'Pause must be reconfirmed after the full old-Lambda lifetime.'
    Assert-Case ($workflow -match 'Configuration.LastUpdateStatus[\s\S]*Code.ImageUri') 'New Lambda state, update and image must be verified.'
    Assert-Case ($workflow -match 'Keep API stopped after failed migration deployment[\s\S]*failure\(\)[\s\S]*docker compose --env-file \.env stop api') 'Failed migration deployment must stop the API without old-image rollback.'
    Assert-Case ($workflow -match 'if \[ -n "\$HORSE_ID_MIGRATION_OPERATION_ID" \]; then[\s\S]*remains paused for explicit verification and resume') 'Migration mode must remain paused after success.'
    Assert-Case ($dockerfile -match 'LABEL com\.horseracingprediction\.identity-migration-guard="v1"') 'Target Docker image must declare the guarded-runtime capability.'
    Assert-Case ($workflow -match 'source: [^\r\n]*horse-id-migration-deploy\.sh') 'Sourceable helper must be distributed through the existing SCP bundle.'

    Write-Output 'PASS T4 deploy guard fixtures, all-store integrity, bounds, backup sidecars, capability, child timeout and workflow order'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}
