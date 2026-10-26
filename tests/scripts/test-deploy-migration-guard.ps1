param(
    [string] $Bash = 'C:\Program Files\Git\bin\bash.exe',
    [string] $Python = 'python3',
    [string] $OperatorFixtureDirectory,
    [switch] $PythonSemanticOnly
)
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$operatorFixtureDirectory = if ([string]::IsNullOrWhiteSpace($OperatorFixtureDirectory)) { $null } else { [IO.Path]::GetFullPath($OperatorFixtureDirectory) }
if (-not $operatorFixtureDirectory) { throw 'A typed operator fixture is required. Export it with HorseIdentityMigrationDeployInteropTests first.' }
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$operatorFixtureName = [IO.Path]::GetFileName($operatorFixtureDirectory)
$operatorFixtureValid = $operatorFixtureDirectory.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) `
    -and $operatorFixtureName.StartsWith('hrp-horse-identity-deploy-interop-', [StringComparison]::Ordinal) `
    -and (Test-Path -LiteralPath $operatorFixtureDirectory -PathType Container)
if ($operatorFixtureValid) {
    $operatorFixtureValid = ((Get-Item -LiteralPath $operatorFixtureDirectory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0
}
if (-not $operatorFixtureValid) {
    throw 'The typed operator fixture must be an existing owned temporary directory.'
}
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
if ($ruby -and -not $PythonSemanticOnly) {
    $yamlSemanticScript = @'
require 'yaml'
document = YAML.parse_file(ARGV.fetch(0))
walk = lambda do |node|
  next if node.nil?
  if node.is_a?(Psych::Nodes::Mapping)
    keys = node.children.each_slice(2).map { |pair| pair.first.value }
    raise 'duplicate YAML mapping key' unless keys.uniq.length == keys.length
  end
  node.children&.each { |child| walk.call(child) }
end
walk.call(document.root)
workflow = YAML.safe_load_file(ARGV.fetch(0), aliases: false)
events = workflow['on'] || workflow[true]
raise 'workflow_dispatch input is not optional string' unless events.dig('workflow_dispatch','inputs','horse_id_migration_operation_id').then { |v| v && v['required'] == false && v['type'] == 'string' }
raise 'recovery input contract is missing' unless events.dig('workflow_dispatch','inputs','api_startup_recovery') == {'description'=>'Restore the API only from the expected blocked deployment; leaves collection paused','required'=>false,'default'=>false,'type'=>'boolean'} && events.dig('workflow_dispatch','inputs','expected_deployed_sha') == {'description'=>'Exact currently deployed API commit SHA required for API startup recovery','required'=>false,'type'=>'string'}
raise 'verification runner must be Ubuntu 24.04' unless workflow.dig('jobs','verify','runs-on') == 'ubuntu-24.04'
verify_steps = workflow.dig('jobs','verify','steps')
guard_step = verify_steps.find { |s| s['name'] == 'Verify horse identity migration deploy guard' }
raise 'migration guard tests need a finite step timeout' unless guard_step && guard_step['timeout-minutes'] == 3
api_fixture_index = verify_steps.index { |s| s['name'] == 'Build the production API test fixture' }
guard_index = verify_steps.index(guard_step)
raise 'the production API test fixture must be built before the interop guard' unless api_fixture_index && guard_index && api_fixture_index < guard_index && verify_steps[api_fixture_index]['run'].include?('HorseRacingPrediction.Api.Tests.csproj') && verify_steps[api_fixture_index]['run'].include?('--configuration Debug')
raise 'build must depend on verification' unless Array(workflow.dig('jobs','build-and-push','needs')).include?('verify')
early_validation = verify_steps.find { |s| s['name'] == 'Validate optional migration request before publishing or deployment' }
raise 'pre-publish validation must reject GUID.Empty' unless early_validation && early_validation['run'].include?('00000000-0000-0000-0000-000000000000')
raise 'recovery input validation must be default-off, manual-only, SHA-bound, and migration-exclusive' unless early_validation['run'].include?('API_STARTUP_RECOVERY') && early_validation['run'].include?('EXPECTED_DEPLOYED_SHA') && early_validation['run'].include?('workflow_dispatch') && early_validation['run'].include?('HORSE_ID_MIGRATION_OPERATION_ID')
operator_input = events.dig('workflow_dispatch','inputs','operator_ui_recovery_operation_id')
raise 'operator UI recovery input contract is missing' unless operator_input == {'description'=>'Existing bound horse-identity operation GUID for protected operator UI bootstrap','required'=>false,'type'=>'string'}
raise 'operator validation must be manual, GUID/SHA-bound, and mutually exclusive' unless early_validation['run'].include?('OPERATOR_UI_RECOVERY_OPERATION_ID') && early_validation['run'].include?('Operator UI recovery requires') && early_validation['run'].include?('EXPECTED_DEPLOYED_SHA')
recovery = workflow.dig('jobs','recover-api-startup')
raise 'API recovery must be a manual API-only route after verified build' unless recovery && recovery['if'].to_s.include?('api_startup_recovery') && Array(recovery['needs']).sort == ['build-and-push','verify'] && recovery['environment'] == 'production' && recovery['timeout-minutes'] == 50
diagnostic = workflow.dig('jobs','diagnose-api-startup')
raise 'read-only diagnosis must be an independent bounded manual job' unless diagnostic && diagnostic['if'].to_s.include?('api_startup_recovery') && !diagnostic.key?('needs') && diagnostic['environment'] == 'production' && diagnostic['timeout-minutes'] == 8
diagnostic_steps = diagnostic['steps']
validation_index = diagnostic_steps.index { |s| s['name'] == 'Validate read-only API diagnosis inputs' }
classify_index = diagnostic_steps.index { |s| s['name'] == 'Classify the expected API container without inspecting its data' }
upload_index = diagnostic_steps.index { |s| s['name'] == 'Stage bounded diagnostic helper outside the application directory' }
diagnostic_step = diagnostic_steps.find { |s| s['name'] == 'Diagnose the exact stopped API without changing production state' }
raise 'diagnosis must validate inputs before SSH and classify before any helper upload' unless validation_index && classify_index && upload_index && diagnostic_step && validation_index < classify_index && classify_index < upload_index && upload_index < diagnostic_steps.index(diagnostic_step)
raise 'running expected image must skip helper upload and data diagnosis' unless diagnostic_steps[upload_index]['if'].to_s.include?('steps.classify.outputs.stdout') && diagnostic_steps[upload_index]['if'].to_s.include?('DIAGNOSTIC code=STOPPED') && diagnostic_step['if'].to_s.include?('DIAGNOSTIC code=STOPPED')
classify_step = diagnostic_steps[classify_index]
raise 'container classification must capture only the fixed state result and avoid data/helper reads' unless classify_step['id'] == 'classify' && classify_step.dig('with','capture_stdout') == true && classify_step.dig('with','command_timeout') == '30s' && classify_step.dig('with','script').include?('running) echo "DIAGNOSTIC code=NOT_APPLICABLE') && classify_step.dig('with','script').include?('exited) echo "DIAGNOSTIC code=STOPPED') && !classify_step.dig('with','script').match?(/horse_id_migration_|docker logs|docker exec|sqlite/i)
raise 'diagnostic helper must be staged outside the application directory from the exact checkout' unless diagnostic_steps[upload_index]['uses'] == 'appleboy/scp-action@v1.0.0' && diagnostic_steps[upload_index].dig('with','source') == 'deploy/horse-id-migration-deploy.sh' && diagnostic_steps[upload_index].dig('with','target').to_s.start_with?('/tmp/hrp-api-diagnostic-')
diagnostic_script = diagnostic_step.dig('with','script')
running_branch = diagnostic_script.index('if [ "$state" = "running" ]; then')
diagnostic_helper = diagnostic_script.index('horse_id_migration_diagnose_startup_journal')
diagnostic_backup = diagnostic_script.index('horse_id_migration_verify_recovery_backup')
raise 'matching running API must exit not-applicable before journal or backup reads' unless running_branch && diagnostic_helper && diagnostic_backup && running_branch < diagnostic_script.index('exit 0', running_branch) && diagnostic_script.index('exit 0', running_branch) < diagnostic_script.index('. "$DIAGNOSTIC_HELPER"') && diagnostic_script.index('. "$DIAGNOSTIC_HELPER"') < diagnostic_helper && diagnostic_helper < diagnostic_backup
raise 'diagnostic path must not start, stop, pull, exec, or mutate service/database configuration' if diagnostic_script.match?(/docker compose[^\n]*(?:\bstop\b|\bup\b|\bpull\b|\bexec\b)|\b(?:sqlite3|UPDATE|INSERT|DELETE|ALTER TABLE|CREATE TABLE|flock)\b|horse_id_migration_(?:acquire|update|copy|release|journal_preflight)/i)
raise 'diagnostic must call bounded read-only journal, backup, and safe log functions' unless diagnostic_script.include?('horse_id_migration_diagnose_startup_journal') && diagnostic_script.include?('horse_id_migration_verify_recovery_backup') && diagnostic_script.include?('horse_id_migration_sanitize_startup_logs')
raise 'diagnostic must preserve both log streams, pipe failures, and still attempt journal/backup evidence before failing' unless diagnostic_script.include?('set -euo pipefail') && diagnostic_script.include?('docker logs --tail 200 "$container" 2>&1 | head') && diagnostic_script.include?('logs_status=$?') && diagnostic_script.index('logs_status=') < diagnostic_script.index('horse_id_migration_diagnose_startup_journal') && diagnostic_script.index('horse_id_migration_verify_recovery_backup') < diagnostic_script.index('[ "$logs_status" -eq 0 ]')
raise 'normal deploy paths must exclude API recovery without changing their requirements' unless workflow.dig('jobs','deploy')['if'].to_s.include?('api_startup_recovery') && workflow.dig('jobs','deploy-collector-lambda')['if'].to_s.include?('api_startup_recovery')
operator = workflow.dig('jobs','recover-operator-ui')
raise 'operator recovery must be an independent verified production job' unless operator && operator['if'].to_s.include?('operator_ui_recovery_operation_id') && Array(operator['needs']).sort == ['build-and-push','verify'] && operator['environment'] == 'production' && operator['timeout-minutes'] == 50
operator_script = operator['steps'].find { |s| s['name'] == 'Bootstrap only the protected operator UI' }.dig('with','script')
raise 'operator recovery must bind target image to the exact workflow SHA' unless operator_script.include?('TARGET_COMMIT_SHA') && operator_script.include?('sha-$TARGET_COMMIT_SHA')
raise 'operator binding and ownership must share the configured physical/logical source pairs' unless operator_script.index('ownership_pairs=()') < operator_script.index('horse_id_migration_validate_operator_binding') && !operator_script.include?('horse_id_migration_validate_operator_binding "$data_directory" "$OPERATOR_UI_RECOVERY_OPERATION_ID" "${source_paths[@]}"')
first_pause = operator_script.index('horse_id_migration_assert_recovery_collection_state')
second_pause = operator_script.index('horse_id_migration_assert_recovery_collection_state', first_pause + 1)
operator_positions = [operator_script.index('horse_id_migration_assert_operator_status'), operator_script.index('horse_id_migration_validate_operator_binding', operator_script.index('horse_id_migration_assert_operator_status')), first_pause, operator_script.index('docker pull '), operator_script.index('horse_id_migration_check_image_capability'), operator_script.index("stage=stopping\ndocker compose --env-file .env stop api"), operator_script.index('horse_id_migration_assert_api_container_exited'), operator_script.index('horse_id_migration_acquire_source_ownership --preserve-existing-maintenance-gates'), operator_script.index('horse_id_migration_acquire_journal_operation_lock'), second_pause, operator_script.rindex('horse_id_migration_validate_operator_binding'), operator_script.index('horse_id_migration_copy_store_set'), operator_script.rindex('horse_id_migration_update_recovery_env'), operator_script.index('HRP_HORSE_IDENTITY_OPERATOR_ONLY'), operator_script.rindex('docker compose --env-file .env -f docker-compose.yml -f "$operator_override" up'), operator_script.index('horse_id_migration_assert_operator_ui_ready')]
raise 'operator recovery must verify, drain, lease, back up, and start protected UI in order' unless operator_positions.all? && operator_positions.each_cons(2).all? { |a,b| a < b }
raise 'operator failure cleanup must be installed before leases, restore old image only after binding recheck, and preserve proven target' unless operator_script.index('trap operator_cleanup EXIT') < operator_script.index('horse_id_migration_acquire_source_ownership') && operator_script.include?('old_restore_override') && operator_script.include?('restored_image_id" != "$old_image_id"') && operator_script.index('target_proven=true') < operator_script.index('horse_id_migration_assert_operator_ui_ready')
raise 'operator rollback must use an exact local image ID with pull disabled, never a config ID as a manifest digest' unless operator_script.include?('docker image tag "$old_image_id" "$old_restore_local_image"') && operator_script.include?('pull_policy: never') && !operator_script.include?('old_image_repository@$old_image_id')
raise 'operator recovery must not call migration, retry, reset, marker deletion, Lambda, or arbitrary exec paths' if operator_script.match?(/horse_id_migration_run_fixed_command|horse-id-migration(?:\s|["']|$)|\b(?:resume|retry|reset|DELETE|terraform|lambda|aws|docker exec)\b/i)
raise 'normal deploy and Lambda paths must exclude operator recovery' unless workflow.dig('jobs','deploy')['if'].to_s.include?('operator_ui_recovery_operation_id') && workflow.dig('jobs','deploy-collector-lambda')['if'].to_s.include?('operator_ui_recovery_operation_id')
raise 'operator recovery must skip unrelated race-owner migration' unless workflow.dig('jobs','migrate-race-entry-owners')['if'].to_s.include?('operator_ui_recovery_operation_id')
upload = recovery['steps'].find { |s| s['name'] == 'Upload only the verified recovery helper' }
recovery_step_index = recovery['steps'].index { |s| s['name'] == 'Recover only the exact blocked API image' }
raise 'recovery must upload only the helper from the checked-out exact SHA before invoking it' unless upload && upload['uses'] == 'appleboy/scp-action@v1.0.0' && upload.dig('with','source') == 'deploy/horse-id-migration-deploy.sh' && upload.dig('with','strip_components') == 1 && recovery_step_index && recovery['steps'].index(upload) < recovery_step_index
recovery_script = recovery.dig('steps').find { |s| s['name'] == 'Recover only the exact blocked API image' }.dig('with','script')
fixed_success_call = '( horse_id_migration_run_fixed_command "$verified_success_guid" "$target_image" "$APP_DIRECTORY/.env" )'
recovery_lines = recovery_script.lines.map(&:strip)
fixed_branch = recovery_lines.index('if [ -n "$verified_success_guid" ]; then')
raise 'recovery must execute the fixed-success verifier exactly once only for a nonempty GUID' unless recovery_script.scan('horse_id_migration_run_fixed_command').length == 1 && recovery_script.split(fixed_success_call, -1).length - 1 == 1 && fixed_branch && recovery_lines[fixed_branch + 1] == fixed_success_call && recovery_lines[fixed_branch + 2] == 'fi'
recovery_state_branch = recovery_script.index('case "$current_state" in')
running_state_branch = recovery_script.index('running)', recovery_state_branch)
running_blocked_check = recovery_script.index('horse_id_migration_assert_api_startup_blocked', running_state_branch)
exited_state_branch = recovery_script.index('exited)', recovery_state_branch)
unsupported_state_branch = recovery_script.index('Existing API container is neither running nor safely stopped.', exited_state_branch)
raise 'recovery must resolve the exact Compose project and enumerate its API containers before branching on state' unless recovery_script.include?('compose_config=') && recovery_script.include?('docker compose --env-file .env config --format json') && recovery_script.include?('expected_compose_project=') && recovery_script.include?('python3 -c') && recovery_script.include?('docker compose --env-file .env ps --all -q api') && recovery_script.include?('{{.State.Status}}') && recovery_script.include?('com.docker.compose.service') && recovery_script.include?('com.docker.compose.project') && recovery_script.include?('[ "$compose_project" = "$expected_compose_project" ]') && recovery_script.index('container_metadata=') < recovery_state_branch
raise 'running recovery must retain the blocked HTTP check while only a verified exited container skips it' unless running_state_branch < running_blocked_check && running_blocked_check < exited_state_branch && exited_state_branch < unsupported_state_branch && recovery_script.index('horse_id_migration_assert_api_startup_blocked') < exited_state_branch
recovery_order = ['expected_current_image=', 'compose_config=', 'docker compose --env-file .env config --format json', 'expected_compose_project=', 'python3 -c', 'unset compose_config', 'docker compose --env-file .env ps --all -q api', 'container_metadata=', 'docker inspect', 'case "$current_state" in', 'horse_id_migration_assert_api_startup_blocked', 'docker compose --env-file .env pull api', 'horse_id_migration_check_image_capability', 'recovery_started=true', 'docker compose --env-file .env stop api', 'horse_id_migration_configured_store_paths', 'horse_id_migration_acquire_source_ownership', 'horse_id_migration_acquire_journal_operation_lock', 'horse_id_migration_assert_recovery_collection_state', 'sleep 900', 'horse_id_migration_assert_recovery_collection_state', 'horse_id_migration_journal_preflight', 'horse_id_migration_copy_store_set', 'RECOVERY-METADATA', 'horse_id_migration_update_recovery_env', 'horse_id_migration_release_source_ownership', fixed_success_call, 'docker compose --env-file .env up -d --no-deps --force-recreate api', 'horse_id_migration_wait_recovery_swagger', 'horse_id_migration_assert_recovery_api_ready']
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
import re,sys,yaml
class StrictLoader(yaml.SafeLoader): pass
def strict_mapping(loader,node,deep=False):
    result={}
    for key_node,value_node in node.value:
        key=loader.construct_object(key_node,deep=deep)
        if key in result: raise ValueError('duplicate YAML mapping key')
        result[key]=loader.construct_object(value_node,deep=deep)
    return result
StrictLoader.add_constructor(yaml.resolver.BaseResolver.DEFAULT_MAPPING_TAG,strict_mapping)
w=yaml.load(open(sys.argv[1],encoding='utf-8'),Loader=StrictLoader)
events=w.get('on',w.get(True))
assert events['workflow_dispatch']['inputs']['horse_id_migration_operation_id']=={'description':'Existing staged horse-identity migration GUID; leave empty for a normal deploy','required':False,'type':'string'}
assert events['workflow_dispatch']['inputs']['api_startup_recovery']=={'description':'Restore the API only from the expected blocked deployment; leaves collection paused','required':False,'default':False,'type':'boolean'}
assert events['workflow_dispatch']['inputs']['expected_deployed_sha']=={'description':'Exact currently deployed API commit SHA required for API startup recovery','required':False,'type':'string'}
assert w['jobs']['verify']['runs-on']=='ubuntu-24.04'
verify=w['jobs']['verify']['steps']
guard_test=next(s for s in verify if s.get('name')=='Verify horse identity migration deploy guard')
assert guard_test['timeout-minutes']==3 and 'verify' in w['jobs']['build-and-push']['needs']
api_fixture_index=next(i for i,s in enumerate(verify) if s.get('name')=='Build the production API test fixture'); guard_index=next(i for i,s in enumerate(verify) if s.get('name')=='Verify horse identity migration deploy guard'); assert api_fixture_index<guard_index and 'HorseRacingPrediction.Api.Tests.csproj' in verify[api_fixture_index]['run'] and '--configuration Debug' in verify[api_fixture_index]['run']
early_validation=next(s for s in verify if s.get('name')=='Validate optional migration request before publishing or deployment')
assert '00000000-0000-0000-0000-000000000000' in early_validation['run']
assert all(x in early_validation['run'] for x in ('API_STARTUP_RECOVERY','EXPECTED_DEPLOYED_SHA','workflow_dispatch','HORSE_ID_MIGRATION_OPERATION_ID'))
assert events['workflow_dispatch']['inputs']['operator_ui_recovery_operation_id']=={'description':'Existing bound horse-identity operation GUID for protected operator UI bootstrap','required':False,'type':'string'}
assert all(x in early_validation['run'] for x in ('OPERATOR_UI_RECOVERY_OPERATION_ID','Operator UI recovery requires','EXPECTED_DEPLOYED_SHA'))
recovery=w['jobs']['recover-api-startup']; assert 'api_startup_recovery' in recovery['if'] and sorted(recovery['needs'])==['build-and-push','verify'] and recovery['environment']=='production' and recovery['timeout-minutes']==50
diagnostic=w['jobs']['diagnose-api-startup']; assert 'api_startup_recovery' in diagnostic['if'] and 'needs' not in diagnostic and diagnostic['environment']=='production' and diagnostic['timeout-minutes']==8
diagnostic_steps=diagnostic['steps']; validation_index=next(i for i,s in enumerate(diagnostic_steps) if s.get('name')=='Validate read-only API diagnosis inputs'); classify_index=next(i for i,s in enumerate(diagnostic_steps) if s.get('name')=='Classify the expected API container without inspecting its data'); upload_index=next(i for i,s in enumerate(diagnostic_steps) if s.get('name')=='Stage bounded diagnostic helper outside the application directory'); diagnostic_step=next(s for s in diagnostic_steps if s.get('name')=='Diagnose the exact stopped API without changing production state'); assert validation_index<classify_index<upload_index<diagnostic_steps.index(diagnostic_step)
assert 'steps.classify.outputs.stdout' in diagnostic_steps[upload_index]['if'] and 'DIAGNOSTIC code=STOPPED' in diagnostic_steps[upload_index]['if'] and 'DIAGNOSTIC code=STOPPED' in diagnostic_step['if']
classify_step=diagnostic_steps[classify_index]; classify_script=classify_step['with']['script']; assert classify_step['id']=='classify' and classify_step['with']['capture_stdout'] is True and classify_step['with']['command_timeout']=='30s' and 'running) echo "DIAGNOSTIC code=NOT_APPLICABLE' in classify_script and 'exited) echo "DIAGNOSTIC code=STOPPED' in classify_script and not re.search(r'horse_id_migration_|docker logs|docker exec|sqlite',classify_script,re.I)
assert diagnostic_steps[upload_index]['uses']=='appleboy/scp-action@v1.0.0' and diagnostic_steps[upload_index]['with']['source']=='deploy/horse-id-migration-deploy.sh' and diagnostic_steps[upload_index]['with']['target'].startswith('/tmp/hrp-api-diagnostic-')
diagnostic_script=diagnostic_step['with']['script']; running_branch=diagnostic_script.index('if [ "$state" = "running" ]; then'); helper_read=diagnostic_script.index('. "$DIAGNOSTIC_HELPER"'); journal_read=diagnostic_script.index('horse_id_migration_diagnose_startup_journal'); backup_read=diagnostic_script.index('horse_id_migration_verify_recovery_backup'); running_exit=diagnostic_script.index('exit 0',running_branch); assert running_branch<running_exit<helper_read<journal_read<backup_read
assert not re.search(r'docker compose[^\n]*(?:\bstop\b|\bup\b|\bpull\b|\bexec\b)|\b(?:sqlite3|UPDATE|INSERT|DELETE|ALTER TABLE|CREATE TABLE|flock)\b|horse_id_migration_(?:acquire|update|copy|release|journal_preflight)',diagnostic_script,re.I)
assert all(x in diagnostic_script for x in ('horse_id_migration_diagnose_startup_journal','horse_id_migration_verify_recovery_backup','horse_id_migration_sanitize_startup_logs'))
assert 'set -euo pipefail' in diagnostic_script and 'docker logs --tail 200 "$container" 2>&1 | head' in diagnostic_script and 'logs_status=$?' in diagnostic_script and diagnostic_script.index('horse_id_migration_verify_recovery_backup')<diagnostic_script.index('[ "$logs_status" -eq 0 ]')
assert 'api_startup_recovery' in w['jobs']['deploy']['if'] and 'api_startup_recovery' in w['jobs']['deploy-collector-lambda']['if']
operator=w['jobs']['recover-operator-ui']; assert 'operator_ui_recovery_operation_id' in operator['if'] and sorted(operator['needs'])==['build-and-push','verify'] and operator['environment']=='production' and operator['timeout-minutes']==50
operator_script=next(s for s in operator['steps'] if s.get('name')=='Bootstrap only the protected operator UI')['with']['script']
assert 'TARGET_COMMIT_SHA' in operator_script and 'sha-$TARGET_COMMIT_SHA' in operator_script
assert operator_script.index('ownership_pairs=()')<operator_script.index('horse_id_migration_validate_operator_binding') and 'horse_id_migration_validate_operator_binding "$data_directory" "$OPERATOR_UI_RECOVERY_OPERATION_ID" "${source_paths[@]}"' not in operator_script
first_pause=operator_script.find('horse_id_migration_assert_recovery_collection_state'); second_pause=operator_script.find('horse_id_migration_assert_recovery_collection_state',first_pause+1)
operator_positions=[operator_script.find('horse_id_migration_assert_operator_status'),operator_script.find('horse_id_migration_validate_operator_binding',operator_script.find('horse_id_migration_assert_operator_status')),first_pause,operator_script.find('docker pull '),operator_script.find('horse_id_migration_check_image_capability'),operator_script.find('stage=stopping\ndocker compose --env-file .env stop api'),operator_script.find('horse_id_migration_assert_api_container_exited'),operator_script.find('horse_id_migration_acquire_source_ownership --preserve-existing-maintenance-gates'),operator_script.find('horse_id_migration_acquire_journal_operation_lock'),second_pause,operator_script.rfind('horse_id_migration_validate_operator_binding'),operator_script.find('horse_id_migration_copy_store_set'),operator_script.rfind('horse_id_migration_update_recovery_env'),operator_script.find('HRP_HORSE_IDENTITY_OPERATOR_ONLY'),operator_script.rfind('docker compose --env-file .env -f docker-compose.yml -f "$operator_override" up'),operator_script.find('horse_id_migration_assert_operator_ui_ready')]; assert operator_positions==sorted(operator_positions)
assert operator_script.index('trap operator_cleanup EXIT')<operator_script.index('horse_id_migration_acquire_source_ownership') and 'old_restore_override' in operator_script and 'restored_image_id" != "$old_image_id"' in operator_script and operator_script.index('target_proven=true')<operator_script.index('horse_id_migration_assert_operator_ui_ready')
assert 'docker image tag "$old_image_id" "$old_restore_local_image"' in operator_script and 'pull_policy: never' in operator_script and 'old_image_repository@$old_image_id' not in operator_script
assert not re.search(r'horse_id_migration_run_fixed_command|horse-id-migration(?:\s|["\']|$)|\b(?:resume|retry|reset|DELETE|terraform|lambda|aws|docker exec)\b',operator_script,re.I)
assert 'operator_ui_recovery_operation_id' in w['jobs']['deploy']['if'] and 'operator_ui_recovery_operation_id' in w['jobs']['deploy-collector-lambda']['if']
assert 'operator_ui_recovery_operation_id' in w['jobs']['migrate-race-entry-owners']['if']
upload=next((s for s in recovery['steps'] if s.get('name')=='Upload only the verified recovery helper'),None); recovery_step_index=next((i for i,s in enumerate(recovery['steps']) if s.get('name')=='Recover only the exact blocked API image'),None)
assert upload and upload['uses']=='appleboy/scp-action@v1.0.0' and upload['with']['source']=='deploy/horse-id-migration-deploy.sh' and upload['with']['strip_components']==1 and recovery_step_index is not None and recovery['steps'].index(upload)<recovery_step_index
recovery_script=next(s for s in recovery['steps'] if s.get('name')=='Recover only the exact blocked API image')['with']['script']
fixed_success_call='( horse_id_migration_run_fixed_command "$verified_success_guid" "$target_image" "$APP_DIRECTORY/.env" )'
assert recovery_script.count('horse_id_migration_run_fixed_command')==1 and recovery_script.count(fixed_success_call)==1
recovery_lines=recovery_script.splitlines(); fixed_branch=next(i for i,line in enumerate(recovery_lines) if line.strip()=='if [ -n "$verified_success_guid" ]; then')
assert recovery_lines[fixed_branch+1].strip()==fixed_success_call and recovery_lines[fixed_branch+2].strip()=='fi'
state_branch=recovery_script.index('case "$current_state" in'); running_branch=recovery_script.index('running)',state_branch); blocked_check=recovery_script.index('horse_id_migration_assert_api_startup_blocked',running_branch); exited_branch=recovery_script.index('exited)',state_branch); unsupported_branch=recovery_script.index('Existing API container is neither running nor safely stopped.',exited_branch)
assert 'compose_config=' in recovery_script and 'docker compose --env-file .env config --format json' in recovery_script and 'expected_compose_project=' in recovery_script and 'python3 -c' in recovery_script and 'docker compose --env-file .env ps --all -q api' in recovery_script and '{{.State.Status}}' in recovery_script and 'com.docker.compose.service' in recovery_script and 'com.docker.compose.project' in recovery_script and '[ "$compose_project" = "$expected_compose_project" ]' in recovery_script and recovery_script.index('container_metadata=')<state_branch
assert running_branch<blocked_check<exited_branch<unsupported_branch and recovery_script.index('horse_id_migration_assert_api_startup_blocked')<exited_branch
recovery_order=('expected_current_image=','compose_config=','docker compose --env-file .env config --format json','expected_compose_project=','python3 -c','unset compose_config','docker compose --env-file .env ps --all -q api','container_metadata=','docker inspect','case "$current_state" in','horse_id_migration_assert_api_startup_blocked','docker compose --env-file .env pull api','horse_id_migration_check_image_capability','recovery_started=true','docker compose --env-file .env stop api','horse_id_migration_configured_store_paths','horse_id_migration_acquire_source_ownership','horse_id_migration_acquire_journal_operation_lock','horse_id_migration_assert_recovery_collection_state','sleep 900','horse_id_migration_assert_recovery_collection_state','horse_id_migration_journal_preflight','horse_id_migration_copy_store_set','RECOVERY-METADATA','horse_id_migration_update_recovery_env','horse_id_migration_release_source_ownership',fixed_success_call,'docker compose --env-file .env up -d --no-deps --force-recreate api','horse_id_migration_wait_recovery_swagger','horse_id_migration_assert_recovery_api_ready')
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
    if ($PythonSemanticOnly) { return }
}

$eligibilityExtractor = @'
import base64,sys,yaml
w=yaml.safe_load(open(sys.argv[1],encoding='utf-8'))
s=next(x for x in w['jobs']['recover-api-startup']['steps'] if x.get('name')=='Recover only the exact blocked API image')['with']['script']
start=s.index('expected_current_image=')
end=s.index('IMAGE_NAME="$requested_image_name"',start)
ready=s.index('horse_id_migration_wait_recovery_swagger "https://$domain"')
ready_end=s.index('\n',s.index('horse_id_migration_assert_recovery_api_ready "https://$domain" "$HORSE_RACING_API_KEY"',ready))
print(base64.b64encode(s[start:end].encode()).decode())
print(base64.b64encode(s[ready:ready_end].encode()).decode())
interop=next(x for x in w['jobs']['verify']['steps'] if x.get('name')=='Verify horse identity migration deploy guard')['run']
print(base64.b64encode(interop.encode()).decode())
'@
if ($ruby) {
    $eligibilityExtractor = @'
require 'yaml'
require 'base64'
w=YAML.safe_load_file(ARGV.fetch(0), aliases: false)
s=w.dig('jobs','recover-api-startup','steps').find { |x| x['name']=='Recover only the exact blocked API image' }.dig('with','script')
start=s.index('expected_current_image=') or abort 'recovery eligibility start missing'
finish=s.index('IMAGE_NAME="$requested_image_name"',start) or abort 'recovery eligibility end missing'
ready=s.index('horse_id_migration_wait_recovery_swagger "https://$domain"') or abort 'shared recovery readiness start missing'
ready_line_end=s.index("\n",s.index('horse_id_migration_assert_recovery_api_ready "https://$domain" "$HORSE_RACING_API_KEY"',ready)) or abort 'shared recovery readiness end missing'
interop=w.dig('jobs','verify','steps').find { |x| x['name']=='Verify horse identity migration deploy guard' } or abort 'typed interop workflow step missing'
puts Base64.strict_encode64(s[start...finish])
puts Base64.strict_encode64(s[ready...ready_line_end])
puts Base64.strict_encode64(interop['run'])
'@
    $eligibilityBase64 = & $ruby.Source -e $eligibilityExtractor $workflowPath 2>&1
}
else {
    $eligibilityBase64 = & $pythonCommand -c $eligibilityExtractor $workflowPath 2>&1
}
Assert-Case ($LASTEXITCODE -eq 0 -and $eligibilityBase64.Count -eq 3) "Could not extract recovery container eligibility, shared readiness, and typed interop step from parsed workflow: $(($eligibilityBase64 -join "`n"))"
$eligibilitySnippet = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($eligibilityBase64[0].Trim()))
$readinessSnippet = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($eligibilityBase64[1].Trim()))
$typedInteropStep = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($eligibilityBase64[2].Trim()))

$pythonBashPath = ConvertTo-BashPath $pythonCommand
$helperBashPath = ConvertTo-BashPath $helperPath
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("hrp-migration-deploy-test-" + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $tempRoot)
$tempBashPath = ConvertTo-BashPath $tempRoot

function Invoke-WorkflowInteropProducer([string] $Payload, [string] $RunnerTemporaryRoot, [string] $DotNetTemporaryRoot, [int] $TimeoutMilliseconds = 180000) {
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo.FileName = (Get-Command pwsh -ErrorAction Stop).Source
    $process.StartInfo.ArgumentList.Add('-NoProfile')
    $process.StartInfo.ArgumentList.Add('-NonInteractive')
    $process.StartInfo.ArgumentList.Add('-EncodedCommand')
    $process.StartInfo.ArgumentList.Add([Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Payload)))
    $process.StartInfo.WorkingDirectory = $repositoryRoot
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardInput = $true
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $process.StartInfo.Environment['RUNNER_TEMP'] = $RunnerTemporaryRoot
    $process.StartInfo.Environment['TMPDIR'] = $DotNetTemporaryRoot
    $process.StartInfo.Environment['TMP'] = $DotNetTemporaryRoot
    $process.StartInfo.Environment['TEMP'] = $DotNetTemporaryRoot
    if (-not $process.Start()) { throw 'Could not start the parsed workflow interop producer.' }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    try {
        $process.StandardInput.Close()
        if (-not $process.WaitForExit($TimeoutMilliseconds)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "Parsed workflow typed producer exceeded ${TimeoutMilliseconds}ms; its process tree was terminated."
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

$interopGuardInvocationPattern = '(?m)^[ \t]*\./tests/scripts/test-deploy-migration-guard\.ps1 -Bash /bin/bash -OperatorFixtureDirectory \$fixtureDirectory\r?\n[ \t]*if \(\$LASTEXITCODE -ne 0\) \{ throw ''Horse identity migration deploy guard failed\.'' \}'
Assert-Case ([regex]::Matches($typedInteropStep, $interopGuardInvocationPattern).Count -eq 1) 'The parsed workflow interop step must contain exactly one guard handoff to isolate its producer in this test.'
$typedInteropProducerStep = [regex]::Replace($typedInteropStep, $interopGuardInvocationPattern, "Write-Output ('PASS typed workflow fixture=' + `$fixtureDirectory)")
$runnerTemporaryRoot = Join-Path $tempRoot ("runner-temp-boundary-" + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $runnerTemporaryRoot)
$dotNetTemporaryRoot = Join-Path $tempRoot ("dotnet-temp-boundary-" + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $dotNetTemporaryRoot)
Assert-Case ([IO.Path]::GetFullPath($runnerTemporaryRoot) -ne [IO.Path]::GetFullPath($dotNetTemporaryRoot)) 'The workflow regression requires RUNNER_TEMP to differ from the .NET temp root.'
$oldRunnerTempProducerStep = $typedInteropProducerStep.Replace('[IO.Path]::GetTempPath()', '$env:RUNNER_TEMP')
Assert-Case ($oldRunnerTempProducerStep -ne $typedInteropProducerStep) 'The parsed workflow step must select the canonical .NET temp root before running the regression.'
Write-Output 'START parsed workflow expected-red legacy RUNNER_TEMP counterexample'
$oldRunnerTempResult = Invoke-WorkflowInteropProducer $oldRunnerTempProducerStep $runnerTemporaryRoot $dotNetTemporaryRoot
Assert-Case ($oldRunnerTempResult.ExitCode -ne 0 -and $oldRunnerTempResult.Output.Contains('must target an owned temporary directory')) "The old RUNNER_TEMP selection must fail in the actual typed producer when the roots differ; exit=$($oldRunnerTempResult.ExitCode), output=$($oldRunnerTempResult.Output)"
Write-Output "PASS parsed workflow expected-red ownership guard rejected legacy path (exit $($oldRunnerTempResult.ExitCode))"
Write-Output 'START parsed workflow expected-green canonical .NET temp path'
$canonicalTempResult = Invoke-WorkflowInteropProducer $typedInteropProducerStep $runnerTemporaryRoot $dotNetTemporaryRoot
Assert-Case ($canonicalTempResult.ExitCode -eq 0 -and $canonicalTempResult.Output.Contains('PASS typed workflow fixture=')) "The parsed workflow must export its typed fixture under .NET temp when RUNNER_TEMP differs: $($canonicalTempResult.Output)"
Write-Output "PASS parsed workflow typed exporter accepted canonical .NET temp path (exit $($canonicalTempResult.ExitCode))"
Write-Output 'START bounded parsed PowerShell child timeout counterexample'
$stallTimedOut = $false
try {
    [void](Invoke-WorkflowInteropProducer 'Start-Sleep -Seconds 10' $runnerTemporaryRoot $dotNetTemporaryRoot 1500)
}
catch {
    $stallTimedOut = $_.Exception.Message.Contains('exceeded 1500ms; its process tree was terminated')
}
Assert-Case $stallTimedOut 'A stalled parsed-workflow child must hit its short timeout and terminate its process tree.'
Write-Output 'PASS bounded parsed PowerShell child timeout terminated stalled process tree'

$operatorYamlStart = $workflow.IndexOf("            bash -s <<'OPERATOR_UI_RECOVERY'", [StringComparison]::Ordinal)
$operatorYamlEnd = $workflow.IndexOf("            OPERATOR_UI_RECOVERY", $operatorYamlStart, [StringComparison]::Ordinal)
Assert-Case ($operatorYamlStart -ge 0 -and $operatorYamlEnd -gt $operatorYamlStart) 'Could not extract the operator recovery script for executable cleanup fixtures.'
$operatorRawLines = $workflow.Substring($operatorYamlStart, $operatorYamlEnd - $operatorYamlStart).Split("`n")
$operatorScriptForExecution = ($operatorRawLines | ForEach-Object {
    if ($_.Length -ge 12) { $_.Substring(12) } else { $_ }
}) -join "`n"
$cleanupStart = $operatorScriptForExecution.IndexOf('operator_cleanup() {', [StringComparison]::Ordinal)
$cleanupEnd = $operatorScriptForExecution.IndexOf('trap operator_cleanup EXIT', $cleanupStart, [StringComparison]::Ordinal)
Assert-Case ($cleanupStart -ge 0 -and $cleanupEnd -gt $cleanupStart) 'Could not extract the parsed operator cleanup function.'
$cleanupFunction = $operatorScriptForExecution.Substring($cleanupStart, $cleanupEnd - $cleanupStart)

function Invoke-OperatorCleanupFixture([string] $Scenario) {
    $fixtureRoot = Join-Path $tempRoot ("operator-cleanup-" + $Scenario)
    [void](New-Item -ItemType Directory -Path $fixtureRoot)
    $logPath = Join-Path $fixtureRoot 'calls.log'
    $overridePath = Join-Path $fixtureRoot '.operator-ui-run-1.old.compose.yml'
    $payload = @'
set +e
LOG=__LOG__
ROOT=__ROOT__
EXPECTED_OVERRIDE=__OVERRIDE__
SCENARIO=__SCENARIO__
: > "$LOG"
docker() {
  printf 'docker %s\n' "$*" >> "$LOG"
  case "$1:$2" in
    image:inspect)
      if [ "${3-}" = "--format" ]; then printf '%s\n' "$OLD_IMAGE_ID"; return 0; fi
      return 1 ;;
    image:tag) OLD_RESTORE_LOCAL_IMAGE="$4"; return 0 ;;
    image:rm) return 0 ;;
    compose:*)
      case "$*" in
        *" up -d "*) grep -Fx '    pull_policy: never' "$EXPECTED_OVERRIDE" >/dev/null || return 91; echo PULL_POLICY_OK >> "$LOG"; return 0 ;;
        *" ps --status running -q api"*) printf 'restored-container\n'; return 0 ;;
        *) return 0 ;;
      esac ;;
    inspect:--format)
      case "$3" in
        *'.Image'*) printf 'sha256:%064d\n' 0 ;;
        *'.Config.Image'*) printf '%s\n' "$OLD_RESTORE_LOCAL_IMAGE" ;;
      esac
      return 0 ;;
  esac
  return 0
}
horse_id_migration_release_source_ownership() { echo RELEASE >> "$LOG"; }
horse_id_migration_validate_operator_binding() { echo BINDING >> "$LOG"; return 0; }
horse_id_migration_update_recovery_env() { echo ENV_RESTORED >> "$LOG"; return 0; }
data_directory="$ROOT/data"
source_paths=("$ROOT/data/eventstore.db")
APP_DIRECTORY="$ROOT"
GITHUB_RUN_ID=run
GITHUB_RUN_ATTEMPT=1
OPERATOR_UI_RECOVERY_OPERATION_ID=00000000-0000-0000-0000-000000000001
container=old-container
operator_override=
old_restore_override=
old_restore_local_image=
old_image_repository=ghcr.io/example/horse-racing-prediction-api
old_image_tag=sha-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
old_image_id=sha256:0000000000000000000000000000000000000000000000000000000000000000
OLD_IMAGE_ID="$old_image_id"
leases_held=false
target_started=false
target_proven=false
stage=before-stop
OLD_RESTORE_LOCAL_IMAGE=
__CLEANUP__
case "$SCENARIO" in
  pre-stop)
    leases_held=true
    false || operator_cleanup
    ;;
  post-stop)
    stage=stopped
    false || operator_cleanup
    ;;
  post-proven)
    target_started=true
    target_proven=true
    operator_override="$ROOT/operator.compose.yml"
    : > "$operator_override"
    false || operator_cleanup
    ;;
esac
'@
    $payload = $payload.Replace('__LOG__', (Quote-Bash (ConvertTo-BashPath $logPath))).Replace('__ROOT__', (Quote-Bash (ConvertTo-BashPath $fixtureRoot))).Replace('__OVERRIDE__', (Quote-Bash (ConvertTo-BashPath $overridePath))).Replace('__SCENARIO__', (Quote-Bash $Scenario)).Replace('__CLEANUP__', $cleanupFunction)
    $result = Invoke-Bash $payload 15000
    $log = if (Test-Path -LiteralPath $logPath) { Get-Content -Raw -LiteralPath $logPath } else { '' }
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    return [pscustomobject]@{ Result = $result; Log = $log }
}

$preStopFixture = Invoke-OperatorCleanupFixture 'pre-stop'
Assert-Case ($preStopFixture.Result.ExitCode -ne 0 -and $preStopFixture.Log -match 'RELEASE' -and $preStopFixture.Log -notmatch 'stop api|image tag| up -d ') 'Pre-stop operator failure must leave the old host untouched.'
$postStopFixture = Invoke-OperatorCleanupFixture 'post-stop'
Assert-Case ($postStopFixture.Result.ExitCode -ne 0 -and $postStopFixture.Log -match 'image tag' -and $postStopFixture.Log -match 'PULL_POLICY_OK' -and $postStopFixture.Log -match 'ENV_RESTORED' -and $postStopFixture.Log -notmatch 'stop api') 'Post-stop/pre-proven operator failure must restore the exact local image and environment.'
$postProvenFixture = Invoke-OperatorCleanupFixture 'post-proven'
Assert-Case ($postProvenFixture.Result.ExitCode -ne 0 -and [string]::IsNullOrEmpty([string]$postProvenFixture.Log)) 'Post-proven HTTP failure must keep the protected target running.'
Write-Output 'PASS executable parsed operator cleanup fixtures pre-stop/post-stop/post-proven'

$lifecycleStart = $operatorScriptForExecution.IndexOf('stage=stopping', [StringComparison]::Ordinal)
$lifecycleEnd = $operatorScriptForExecution.IndexOf('stage=target-proven', $lifecycleStart, [StringComparison]::Ordinal)
Assert-Case ($lifecycleStart -ge 0 -and $lifecycleEnd -gt $lifecycleStart) 'Could not extract the parsed operator lifecycle for lease-order fixtures.'
$operatorLifecycle = $operatorScriptForExecution.Substring($lifecycleStart, $lifecycleEnd - $lifecycleStart)
function Invoke-OperatorLifecycleFixture([bool] $ExternalWriterBusy) {
    $fixtureName = if ($ExternalWriterBusy) { 'operator-lifecycle-external-writer' } else { 'operator-lifecycle-stop-before-lease' }
    $fixtureRoot = Join-Path $tempRoot $fixtureName
    [void](New-Item -ItemType Directory -Path $fixtureRoot)
    $logPath = Join-Path $fixtureRoot 'calls.log'
    $payload = @'
set +e
LOG=__LOG__
: > "$LOG"
OLD_ROOT_RUNNING=1
EXTERNAL_WRITER_BUSY=__BUSY__
docker() {
  printf 'docker %s\n' "$*" >> "$LOG"
  if [ "$1" = compose ]; then
    case "$*" in
      *" stop api"*) OLD_ROOT_RUNNING=0; echo STOP >> "$LOG"; return 0 ;;
      *" ps --status running -q api"*) printf 'target-container\n'; return 0 ;;
      *" up -d "*) echo UP >> "$LOG"; return 0 ;;
      *) return 0 ;;
    esac
  fi
  if [ "$1" = inspect ]; then
    case "\${3-}" in
      *'.Config.Image {{.Image}}'*) printf '%s %s\n' "$target_image" "$target_image_id"; return 0 ;;
      *'.Config.Image'*) printf '%s\n' "$target_image"; return 0 ;;
      *'.Image'*) printf '%s\n' "$target_image_id"; return 0 ;;
      *'range .Config.Env'*) printf 'HRP_HORSE_IDENTITY_OPERATOR_ONLY=true\n'; return 0 ;;
    esac
  fi
  return 0
}
horse_id_migration_assert_api_container_exited() { [ "$OLD_ROOT_RUNNING" = 0 ]; }
horse_id_migration_assert_recovery_collection_state() { printf '%s\n' '2026-10-25T10:00:00.0000000'; }
horse_id_migration_validate_operator_binding() { echo BINDING >> "$LOG"; return 0; }
horse_id_migration_acquire_source_ownership() { [ "$OLD_ROOT_RUNNING" = 0 ] || return 81; echo SOURCE >> "$LOG"; return 0; }
horse_id_migration_acquire_journal_operation_lock() { [ "$OLD_ROOT_RUNNING" = 0 ] || return 82; echo JOURNAL >> "$LOG"; return 0; }
horse_id_migration_copy_store_set() { echo BACKUP >> "$LOG"; [ "$EXTERNAL_WRITER_BUSY" = 0 ]; }
horse_id_migration_update_recovery_env() { echo ENV >> "$LOG"; return 0; }
horse_id_migration_release_source_ownership() { echo RELEASE >> "$LOG"; return 0; }
horse_id_migration_assert_operator_ui_ready() { return 0; }
APP_DIRECTORY=__ROOT__
GITHUB_RUN_ID=run
GITHUB_RUN_ATTEMPT=1
OPERATOR_UI_RECOVERY_OPERATION_ID=00000000-0000-0000-0000-000000000001
HORSE_RACING_API_KEY=fixture-key
domain=example.test
data_directory="$APP_DIRECTORY/data"
collection_database="$data_directory/collection.db"
configured_stores=(eventstore.db collection.db horse.db horse-identity-migration-journal.db)
source_relatives=(eventstore.db collection.db horse.db)
source_paths=("$data_directory/eventstore.db" "$data_directory/collection.db" "$data_directory/horse.db")
container=old-container
target_image=ghcr.io/example/horse-racing-prediction-api:sha-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
target_image_id=sha256:1111111111111111111111111111111111111111111111111111111111111111
IMAGE_NAME=ghcr.io/example/horse-racing-prediction-api
IMAGE_TAG=sha-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
operator_override=__ROOT__/operator.compose.yml
pause_before_stop=2026-10-25T10:00:00.0000000
old_image_id=sha256:0000000000000000000000000000000000000000000000000000000000000000
mkdir -p "$data_directory"
( set -euo pipefail
__LIFECYCLE__
)
status=$?
printf 'STATUS=%s\n' "$status" >> "$LOG"
exit 0
'@
    $payload = $payload.Replace('__LOG__', (Quote-Bash (ConvertTo-BashPath $logPath))).Replace('__ROOT__', (Quote-Bash (ConvertTo-BashPath $fixtureRoot))).Replace('__BUSY__', $(if ($ExternalWriterBusy) { '1' } else { '0' })).Replace('__LIFECYCLE__', $operatorLifecycle)
    $result = Invoke-Bash $payload 15000
    $log = if (Test-Path -LiteralPath $logPath) { Get-Content -Raw -LiteralPath $logPath } else { '' }
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    return [pscustomobject]@{ Result = $result; Log = $log }
}
$orderedLifecycleFixture = Invoke-OperatorLifecycleFixture $false
$orderedLog = $orderedLifecycleFixture.Log
Assert-Case ($orderedLifecycleFixture.Result.ExitCode -eq 0 -and $orderedLog.IndexOf('STOP') -ge 0 -and $orderedLog.IndexOf('SOURCE') -gt $orderedLog.IndexOf('STOP') -and $orderedLog.IndexOf('JOURNAL') -gt $orderedLog.IndexOf('SOURCE') -and $orderedLog.IndexOf('BACKUP') -gt $orderedLog.IndexOf('JOURNAL') -and $orderedLog.IndexOf('UP') -gt $orderedLog.IndexOf('BACKUP')) 'The parsed operator lifecycle must stop/reap the old controller before acquiring leases and starting the replacement.'
$externalWriterFixture = Invoke-OperatorLifecycleFixture $true
Assert-Case ($externalWriterFixture.Result.ExitCode -eq 0 -and $externalWriterFixture.Log -match 'STOP' -and $externalWriterFixture.Log -match 'SOURCE' -and $externalWriterFixture.Log -match 'JOURNAL' -and $externalWriterFixture.Log -match 'BACKUP' -and $externalWriterFixture.Log -notmatch '(^|\r?\n)UP(\r?\n|$)') 'An external writer after stop must fail backup before replacement start.'
Write-Output 'PASS executable parsed operator lifecycle stop-before-lease and external-writer fixtures'

$validationMatch = [regex]::Match($workflow, '(?s)- name: Validate optional migration request before publishing or deployment\r?\n        shell: bash\r?\n        env:.*?        run: \|\r?\n(?<script>.*?)(?=\r?\n\r?\n      - (?:name: Verify deployment pause|uses: actions/setup-dotnet))')
Assert-Case $validationMatch.Success 'Optional recovery validation script was not found.'
$validationScript = ($validationMatch.Groups['script'].Value -split '\r?\n' | ForEach-Object { $_ -replace '^          ', '' }) -join "`n"
function Invoke-ValidationCase([string] $Operator, [string] $ExpectedSha, [string] $ApiRecovery, [string] $Migration) {
    $result = Invoke-Bash @"
set -euo pipefail
export GITHUB_EVENT_NAME=workflow_dispatch API_STARTUP_RECOVERY=$(Quote-Bash $ApiRecovery) EXPECTED_DEPLOYED_SHA=$(Quote-Bash $ExpectedSha) OPERATOR_UI_RECOVERY_OPERATION_ID=$(Quote-Bash $Operator) HORSE_ID_MIGRATION_OPERATION_ID=$(Quote-Bash $Migration)
$validationScript
"@ 10000
    return $result
}
$validOperatorValidation = Invoke-ValidationCase '00000000-0000-0000-0000-000000000001' ('a' * 40) 'false' ''
Assert-Case ($validOperatorValidation.ExitCode -eq 0) "Valid operator input must pass pre-publish validation: $($validOperatorValidation.Output)"
$missingShaValidation = Invoke-ValidationCase '00000000-0000-0000-0000-000000000001' '' 'false' ''
Assert-Case ($missingShaValidation.ExitCode -eq 2) 'Operator input without expected SHA must be rejected by executable validation.'
$mixedModeValidation = Invoke-ValidationCase '00000000-0000-0000-0000-000000000001' ('a' * 40) 'true' ''
Assert-Case ($mixedModeValidation.ExitCode -eq 2) 'Operator and API startup recovery inputs must be mutually exclusive.'
$mixedMigrationValidation = Invoke-ValidationCase '00000000-0000-0000-0000-000000000001' ('a' * 40) 'false' '00000000-0000-0000-0000-000000000002'
Assert-Case ($mixedMigrationValidation.ExitCode -eq 2) 'Operator and migration inputs must be mutually exclusive.'
$shaOnlyValidation = Invoke-ValidationCase '' ('a' * 40) 'false' ''
Assert-Case ($shaOnlyValidation.ExitCode -eq 2) 'Expected SHA without an explicit recovery mode must remain rejected.'

function Invoke-RecoveryEligibilityCase([string] $ApiIds, [string] $Image, [string] $State, [string] $Service, [string] $ProjectLabel, [string] $ConfigProject, [int] $BlockedStatus) {
    $payload = @"
set -euo pipefail
export IMAGE_NAME='ghcr.io/example/horse-racing-prediction-api' IMAGE_TAG='sha-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' EXPECTED_DEPLOYED_SHA='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' APP_DIRECTORY='/tmp/hrp-recovery-eligibility-test' DOMAIN_NAME='recovery.example.test' LIGHTSAIL_HOST='unused.example.test' HORSE_RACING_API_KEY='test-only'
requested_image_name=`$IMAGE_NAME
requested_image_tag=`$IMAGE_TAG
export MOCK_API_IDS=$(Quote-Bash $ApiIds) MOCK_IMAGE=$(Quote-Bash $Image) MOCK_STATE=$(Quote-Bash $State) MOCK_SERVICE=$(Quote-Bash $Service) MOCK_PROJECT_LABEL=$(Quote-Bash $ProjectLabel) MOCK_CONFIG_PROJECT=$(Quote-Bash $ConfigProject) MOCK_BLOCKED_STATUS=$(Quote-Bash ([string]$BlockedStatus)) MOCK_PYTHON=$(Quote-Bash $pythonBashPath)
docker() {
  case "`$1" in
    compose)
      if [ "`$4" = config ]; then printf '{"name":"%s"}\n' "`$MOCK_CONFIG_PROJECT"
      else printf '%s\n' "`$MOCK_API_IDS"; fi
      ;;
    inspect) printf '%s %s %s %s\n' "`$MOCK_IMAGE" "`$MOCK_STATE" "`$MOCK_SERVICE" "`$MOCK_PROJECT_LABEL" ;;
    *) return 90 ;;
  esac
}
python3() { "`$MOCK_PYTHON" "`$@"; }
horse_id_migration_assert_api_startup_blocked() { printf 'BLOCKED_HTTP_CHECK\n'; return "`$MOCK_BLOCKED_STATUS"; }
horse_id_migration_wait_recovery_swagger() { printf 'SWAGGER_URL=%s\n' "`$1"; }
horse_id_migration_assert_recovery_api_ready() { printf 'READY_URL=%s\nAPI_KEY=%s\n' "`$1" "`$2"; }
$eligibilitySnippet
$readinessSnippet
printf 'ELIGIBILITY_COMPLETE\n'
"@
    return Invoke-Bash $payload
}

$expectedRecoveryImage = 'ghcr.io/example/horse-racing-prediction-api:sha-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$runningEligibility = Invoke-RecoveryEligibilityCase 'api1' $expectedRecoveryImage 'running' 'api' 'project1' 'project1' 0
Assert-Case ($runningEligibility.ExitCode -eq 0 -and $runningEligibility.Output -match 'BLOCKED_HTTP_CHECK' -and $runningEligibility.Output -match 'SWAGGER_URL=https://recovery\.example\.test' -and $runningEligibility.Output -match 'READY_URL=https://recovery\.example\.test' -and $runningEligibility.Output -match 'API_KEY=test-only') "Running API must retain blocked HTTP and shared readiness must receive the resolved URL and key under nounset. $($runningEligibility.Output)"
$stoppedEligibility = Invoke-RecoveryEligibilityCase 'api1' $expectedRecoveryImage 'exited' 'api' 'project1' 'project1' 0
Assert-Case ($stoppedEligibility.ExitCode -eq 0 -and $stoppedEligibility.Output -notmatch 'BLOCKED_HTTP_CHECK' -and $stoppedEligibility.Output -match 'SWAGGER_URL=https://recovery\.example\.test' -and $stoppedEligibility.Output -match 'READY_URL=https://recovery\.example\.test' -and $stoppedEligibility.Output -match 'API_KEY=test-only' -and $stoppedEligibility.Output -match 'ELIGIBILITY_COMPLETE') "Only the exact stopped Compose API may skip the impossible blocked HTTP check, then pass the shared readiness arguments under nounset. $($stoppedEligibility.Output)"
foreach ($case in @(
    @{ Name = 'missing-container'; Ids = ''; Image = $expectedRecoveryImage; State = 'exited'; Service = 'api'; Project = 'project1'; ConfigProject = 'project1'; Blocked = 0 },
    @{ Name = 'multiple-containers'; Ids = "api1`napi2"; Image = $expectedRecoveryImage; State = 'exited'; Service = 'api'; Project = 'project1'; ConfigProject = 'project1'; Blocked = 0 },
    @{ Name = 'wrong-image'; Ids = 'api1'; Image = 'ghcr.io/example/horse-racing-prediction-api:sha-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'; State = 'exited'; Service = 'api'; Project = 'project1'; ConfigProject = 'project1'; Blocked = 0 },
    @{ Name = 'unowned-service'; Ids = 'api1'; Image = $expectedRecoveryImage; State = 'exited'; Service = 'other'; Project = 'project1'; ConfigProject = 'project1'; Blocked = 0 },
    @{ Name = 'incomplete-inspection'; Ids = 'api1'; Image = $expectedRecoveryImage; State = 'exited'; Service = ''; Project = 'project1'; ConfigProject = 'project1'; Blocked = 0 },
    @{ Name = 'wrong-compose-project'; Ids = 'api1'; Image = $expectedRecoveryImage; State = 'exited'; Service = 'api'; Project = 'project-other'; ConfigProject = 'project1'; Blocked = 0 },
    @{ Name = 'missing-compose-project'; Ids = 'api1'; Image = $expectedRecoveryImage; State = 'exited'; Service = 'api'; Project = ''; ConfigProject = 'project1'; Blocked = 0 },
    @{ Name = 'unsupported-state'; Ids = 'api1'; Image = $expectedRecoveryImage; State = 'created'; Service = 'api'; Project = 'project1'; ConfigProject = 'project1'; Blocked = 0 },
    @{ Name = 'running-without-blocked-response'; Ids = 'api1'; Image = $expectedRecoveryImage; State = 'running'; Service = 'api'; Project = 'project1'; ConfigProject = 'project1'; Blocked = 1 }
)) {
    $result = Invoke-RecoveryEligibilityCase $case.Ids $case.Image $case.State $case.Service $case.Project $case.ConfigProject $case.Blocked
    $mustCallBlocked = $case.Name -eq 'running-without-blocked-response'
    Assert-Case ($result.ExitCode -ne 0 -and (($result.Output -match 'BLOCKED_HTTP_CHECK') -eq $mustCallBlocked) -and $result.Output -notmatch 'ELIGIBILITY_COMPLETE') "Recovery eligibility counterexample '$($case.Name)' was not rejected safely. $($result.Output)"
}
Write-Output 'PASS parsed recovery eligibility running/stopped branches and unsafe-container counterexamples'

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

function Write-SupervisorOwnerMarker([string] $Path, [string] $Purpose, [string] $Logical) {
    $sourceHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Logical)))
    if ($Purpose -eq 'api-bootstrap') {
        $fields = @('v1', $Purpose, '-', '-', $sourceHash, '1', '1', ('A' * 64))
    }
    else {
        $fields = @('v1', $Purpose, '00000000-0000-0000-0000-000000000001', ('B' * 64), $sourceHash, '1', '1', ('C' * 64))
    }
    [IO.File]::WriteAllText($Path, ($fields -join "`n"), [Text.Encoding]::ASCII)
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

$diagnosticJournalSeed = @'
import sqlite3,sys
path=sys.argv[1]
db=sqlite3.connect(path)
db.executescript('''
CREATE TABLE migration_operations (operation_id TEXT PRIMARY KEY NOT NULL, request_hash TEXT NOT NULL, request_json TEXT NOT NULL, status INTEGER NOT NULL, reason INTEGER NOT NULL, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, terminal_utc TEXT NULL, results_json TEXT NOT NULL, workflow_claimed INTEGER NOT NULL DEFAULT 0);
CREATE TABLE migration_pages (operation_id TEXT NOT NULL, page_identity TEXT NOT NULL, surface INTEGER NOT NULL, table_name TEXT NOT NULL, row_count INTEGER NOT NULL, membership_hash TEXT NOT NULL, before_hash TEXT NOT NULL, expected_after_hash TEXT NOT NULL, state INTEGER NOT NULL, updated_utc TEXT NOT NULL, PRIMARY KEY(operation_id,page_identity));
CREATE TABLE migration_required_pages (operation_id TEXT NOT NULL, page_identity TEXT NOT NULL, surface INTEGER NOT NULL, table_name TEXT NOT NULL, row_count INTEGER NOT NULL, membership_hash TEXT NOT NULL, before_hash TEXT NOT NULL, expected_after_hash TEXT NOT NULL, evidence_bytes INTEGER NOT NULL, PRIMARY KEY(operation_id,page_identity));
CREATE TABLE migration_request_keys (request_key TEXT PRIMARY KEY NOT NULL, immutable_request_hash TEXT NOT NULL, preview_fingerprint TEXT NULL, request_kind TEXT NOT NULL DEFAULT 'BoundV1', operation_id TEXT NOT NULL UNIQUE);
CREATE TABLE migration_execution_intents (operation_id TEXT PRIMARY KEY NOT NULL, immutable_request_hash TEXT NOT NULL, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, phase INTEGER NOT NULL);
CREATE TABLE migration_pending_seals (operation_id TEXT PRIMARY KEY NOT NULL, immutable_request_hash TEXT NOT NULL, full_fingerprint TEXT NOT NULL, source_set_hash TEXT NOT NULL, backup_manifest_hash TEXT NOT NULL, sealed_utc TEXT NOT NULL);
''')
db.commit();db.close()
'@

function New-DiagnosticJournalFixture([string] $Name) {
    $directory = Join-Path $tempRoot $Name
    [void](New-Item -ItemType Directory -Path $directory)
    [IO.File]::WriteAllBytes((Join-Path $directory 'horse-identity-migration-initialized.marker'), [Text.Encoding]::ASCII.GetBytes('horse-identity-migration-journal-v1'))
    [IO.File]::WriteAllBytes((Join-Path $directory 'horse-identity-migration-operation.lock'), [byte[]]::new(0))
    & $pythonCommand -c $diagnosticJournalSeed (Join-Path $directory 'horse-identity-migration-journal.db')
    if ($LASTEXITCODE -ne 0) { throw "Could not seed read-only startup diagnostic fixture $Name." }
    return $directory
}

function Invoke-StartupJournalDiagnostic([string] $Directory) {
    $directoryBash = ConvertTo-BashPath $Directory
    Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
export HRP_PYTHON3=$(Quote-Bash $pythonBashPath)
horse_id_migration_diagnose_startup_journal $(Quote-Bash $directoryBash)
"@
}

function Get-DiagnosticJournalHashes([string] $Directory) {
    $files = @(
        (Join-Path $Directory 'horse-identity-migration-initialized.marker'),
        (Join-Path $Directory 'horse-identity-migration-operation.lock'),
        (Join-Path $Directory 'horse-identity-migration-journal.db')
    )
    return (($files | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }) -join ',')
}

function Invoke-RecoveryBackupDiagnostic([string] $Directory, [string] $Sha, [string[]] $ExpectedMembers) {
    $directoryBash = ConvertTo-BashPath $Directory
    $memberArguments = ($ExpectedMembers | ForEach-Object { Quote-Bash $_ }) -join ' '
    Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
export HRP_PYTHON3=$(Quote-Bash $pythonBashPath)
horse_id_migration_verify_recovery_backup $(Quote-Bash $directoryBash) $(Quote-Bash $Sha) $memberArguments
"@
}

try {
    $diagnosticJournal = New-DiagnosticJournalFixture 'readonly-startup-journal'
    $diagnosticJournalFiles = @(
        (Join-Path $diagnosticJournal 'horse-identity-migration-initialized.marker'),
        (Join-Path $diagnosticJournal 'horse-identity-migration-operation.lock'),
        (Join-Path $diagnosticJournal 'horse-identity-migration-journal.db')
    )
    $journalHashesBefore = @($diagnosticJournalFiles | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash })
    $journalDiagnostic = Invoke-StartupJournalDiagnostic $diagnosticJournal
    Assert-Case ($journalDiagnostic.ExitCode -eq 0 -and $journalDiagnostic.Output -match 'JOURNAL code=READY_SHAPE marker=1 integrity=ok schema=current request_key_rows=0 staged=0 running=0') "Known ready journal shape must produce bounded read-only evidence. $($journalDiagnostic.Output)"
    $journalHashesAfter = @($diagnosticJournalFiles | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash })
    Assert-Case (($journalHashesBefore -join ',') -eq ($journalHashesAfter -join ',')) 'Startup journal diagnostics must leave marker, lock, and SQLite bytes unchanged.'

    $absentRequestJournal = New-DiagnosticJournalFixture 'request-key-table-absent'
    $absentRequestDatabase = Join-Path $absentRequestJournal 'horse-identity-migration-journal.db'
    & $pythonCommand -c 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute("INSERT INTO migration_operations VALUES (?,?,?,?,?,?,?,?,?,?)", ("00000000-0000-0000-0000-000000000001", "a"*64, "{}", 0, 0, "2026-10-24T00:00:00Z", "2026-10-24T00:00:00Z", None, "[]", 0)); c.execute("DROP TABLE migration_request_keys"); c.commit(); c.close()' $absentRequestDatabase
    Assert-Case ($LASTEXITCODE -eq 0) 'Could not create absent request-key table fixture.'
    $absentRequestHashesBefore = Get-DiagnosticJournalHashes $absentRequestJournal
    $absentRequestResult = Invoke-StartupJournalDiagnostic $absentRequestJournal
    Assert-Case ($absentRequestResult.ExitCode -ne 0 -and $absentRequestResult.Output -match 'JOURNAL code=REQUEST_SCHEMA_UNKNOWN key_table=0 request_key=0 immutable_request_hash=0 operation_id=0 preview_fingerprint=0 request_kind=0 rowcount=unknown staged=1 running=0 succeeded=0 rejected=0 failed=0 interrupted=0 partial=0') 'An absent request-key table must have distinct bounded flags, operation counts, and remain fail-closed.'
    Assert-Case ($absentRequestHashesBefore -eq (Get-DiagnosticJournalHashes $absentRequestJournal)) 'Absent-table diagnosis must not mutate marker, lock, or SQLite bytes.'

    $missingBaseColumnJournal = New-DiagnosticJournalFixture 'request-key-base-column-missing'
    $missingBaseColumnDatabase = Join-Path $missingBaseColumnJournal 'horse-identity-migration-journal.db'
    & $pythonCommand -c 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute("INSERT INTO migration_operations VALUES (?,?,?,?,?,?,?,?,?,?)", ("00000000-0000-0000-0000-000000000001", "a"*64, "{}", 0, 0, "2026-10-24T00:00:00Z", "2026-10-24T00:00:00Z", None, "[]", 0)); c.execute("DROP TABLE migration_request_keys"); c.execute("CREATE TABLE migration_request_keys (request_key TEXT PRIMARY KEY NOT NULL, preview_fingerprint TEXT NULL, request_kind TEXT NOT NULL DEFAULT ''BoundV1'', operation_id TEXT NOT NULL UNIQUE)"); c.commit(); c.close()' $missingBaseColumnDatabase
    Assert-Case ($LASTEXITCODE -eq 0) 'Could not create request-key base-column fixture.'
    $missingBaseHashesBefore = Get-DiagnosticJournalHashes $missingBaseColumnJournal
    $missingBaseResult = Invoke-StartupJournalDiagnostic $missingBaseColumnJournal
    Assert-Case ($missingBaseResult.ExitCode -ne 0 -and $missingBaseResult.Output -match 'JOURNAL code=REQUEST_SCHEMA_UNKNOWN key_table=1 request_key=1 immutable_request_hash=0 operation_id=1 preview_fingerprint=1 request_kind=1 rowcount=unknown staged=1 running=0 succeeded=0 rejected=0 failed=0 interrupted=0 partial=0') "A missing immutable-request-hash column must have distinct bounded flags, operation counts, and remain fail-closed. $($missingBaseResult.Output)"
    Assert-Case ($missingBaseHashesBefore -eq (Get-DiagnosticJournalHashes $missingBaseColumnJournal)) 'Malformed-table diagnosis must not mutate marker, lock, or SQLite bytes.'

    $unknownJournal = Join-Path $tempRoot 'readonly-startup-journal-unknown'
    [void](New-Item -ItemType Directory -Path $unknownJournal)
    [IO.File]::WriteAllBytes((Join-Path $unknownJournal 'horse-identity-migration-initialized.marker'), [Text.Encoding]::ASCII.GetBytes('horse-identity-migration-journal-v1'))
    [IO.File]::WriteAllBytes((Join-Path $unknownJournal 'horse-identity-migration-operation.lock'), [byte[]]::new(0))
    & $pythonCommand -c 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute("CREATE TABLE unrecognized(value TEXT)"); c.close()' (Join-Path $unknownJournal 'horse-identity-migration-journal.db')
    Assert-Case ($LASTEXITCODE -eq 0) 'Could not create unknown journal schema fixture.'
    $unknownHashBefore = (Get-FileHash -LiteralPath (Join-Path $unknownJournal 'horse-identity-migration-journal.db') -Algorithm SHA256).Hash
    $unknownResult = Invoke-StartupJournalDiagnostic $unknownJournal
    Assert-Case ($unknownResult.ExitCode -ne 0 -and $unknownResult.Output -match 'JOURNAL code=SCHEMA_UNKNOWN' -and $unknownResult.Output -notmatch 'unrecognized|CREATE TABLE|Traceback|sqlite3\.') 'Unknown journal schema must emit a bounded failure category without source data.'
    Assert-Case ($unknownHashBefore -eq (Get-FileHash -LiteralPath (Join-Path $unknownJournal 'horse-identity-migration-journal.db') -Algorithm SHA256).Hash) 'Unknown-schema diagnostics must not mutate SQLite bytes.'

    $sidecarJournal = New-DiagnosticJournalFixture 'readonly-startup-journal-sidecar'
    [IO.File]::WriteAllBytes((Join-Path $sidecarJournal 'horse-identity-migration-journal.db-wal'), [byte[]]::new(0))
    $sidecarResult = Invoke-StartupJournalDiagnostic $sidecarJournal
    Assert-Case ($sidecarResult.ExitCode -eq 0 -and $sidecarResult.Output -match 'JOURNAL code=SQLITE_SIDECAR_PRESENT rollback=0 wal=1 shm=0') "SQLite sidecars must be reported without opening or changing the database. $($sidecarResult.Output)"

    $backupData = Join-Path $tempRoot 'readonly-backup-data'
    $backupRoot = Join-Path $backupData 'backups'
    $backupDirectory = Join-Path $backupRoot 'api-startup-recovery-fixture-1'
    [void](New-Item -ItemType Directory -Path $backupDirectory -Force)
    $targetSha = 'a' * 40
    $priorSha = 'b' * 40
    [IO.File]::WriteAllText((Join-Path $backupDirectory 'RECOVERY-METADATA'), "expected_deployed_sha=$priorSha`ntarget_commit_sha=$targetSha`ncreated_utc=2026-10-24T00:00:00Z`n", [Text.Encoding]::ASCII)
    $backupMember = Join-Path $backupDirectory 'journal.db'
    [IO.File]::WriteAllBytes($backupMember, [Text.Encoding]::ASCII.GetBytes('fixture-backup-bytes'))
    $memberHash = (Get-FileHash -LiteralPath $backupMember -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $backupDirectory 'SHA256SUMS'), "$memberHash  journal.db`n", [Text.Encoding]::ASCII)
    $backupHashesBefore = @((Get-FileHash -LiteralPath $backupMember -Algorithm SHA256).Hash, (Get-FileHash -LiteralPath (Join-Path $backupDirectory 'SHA256SUMS') -Algorithm SHA256).Hash, (Get-FileHash -LiteralPath (Join-Path $backupDirectory 'RECOVERY-METADATA') -Algorithm SHA256).Hash)
    $backupDiagnostic = Invoke-RecoveryBackupDiagnostic $backupData $targetSha 'journal.db'
    Assert-Case ($backupDiagnostic.ExitCode -eq 0 -and $backupDiagnostic.Output -match 'BACKUP code=VERIFIED matches=1 files=1 bytes=20') "Exact matching backup metadata and manifest must verify without exposing paths. $($backupDiagnostic.Output)"
    $wrongBackup = Invoke-RecoveryBackupDiagnostic $backupData ('c' * 40) 'journal.db'
    Assert-Case ($wrongBackup.ExitCode -ne 0 -and $wrongBackup.Output -match 'BACKUP code=MATCH_COUNT') 'Backup with a different target SHA must not be accepted.'
    $incompleteBackup = Invoke-RecoveryBackupDiagnostic $backupData $targetSha @('journal.db', 'missing-configured-store.db')
    Assert-Case ($incompleteBackup.ExitCode -ne 0 -and $incompleteBackup.Output -match 'BACKUP code=MEMBERSHIP_INCOMPLETE') 'Backup missing a configured member must fail closed.'
    $secondBackupDirectory = Join-Path $backupRoot 'api-startup-recovery-fixture-2'
    Copy-Item -LiteralPath $backupDirectory -Destination $secondBackupDirectory -Recurse
    $multipleBackups = Invoke-RecoveryBackupDiagnostic $backupData $targetSha 'journal.db'
    Assert-Case ($multipleBackups.ExitCode -eq 0 -and $multipleBackups.Output -match 'BACKUP code=VERIFIED matches=2 files=2 bytes=40') 'All bounded matching backups must be verified and counted without selecting one.'
    Remove-Item -LiteralPath $secondBackupDirectory -Recurse -Force
    [IO.File]::AppendAllText($backupMember, 'changed')
    $changedBackup = Invoke-RecoveryBackupDiagnostic $backupData $targetSha 'journal.db'
    Assert-Case ($changedBackup.ExitCode -ne 0 -and $changedBackup.Output -match 'BACKUP code=HASH_MISMATCH' -and $changedBackup.Output -notmatch [regex]::Escape($backupDirectory)) 'Changed backup bytes must be rejected without outputting paths.'
    Assert-Case ($backupHashesBefore[1] -eq (Get-FileHash -LiteralPath (Join-Path $backupDirectory 'SHA256SUMS') -Algorithm SHA256).Hash -and $backupHashesBefore[2] -eq (Get-FileHash -LiteralPath (Join-Path $backupDirectory 'RECOVERY-METADATA') -Algorithm SHA256).Hash) 'Backup verification must not alter its manifest or metadata.'

    $sanitizedLogs = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
export HRP_PYTHON3=$(Quote-Bash $pythonBashPath)
horse_id_migration_sanitize_startup_logs <<'LOGS'
Unhandled exception: InvalidDataException SQLite Error 14 secret-canary-DO-NOT-PRINT
LOGS
"@
    Assert-Case ($sanitizedLogs.ExitCode -eq 0 -and $sanitizedLogs.Output -match 'InvalidDataException:1' -and $sanitizedLogs.Output -match 'sqlite_codes=14' -and $sanitizedLogs.Output -notmatch 'secret-canary|Unhandled exception|SQLite Error') 'Startup logs must emit only allowlisted exception/code categories.'
    $sanitizedBothStreams = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
export HRP_PYTHON3=$(Quote-Bash $pythonBashPath)
combined=`$( { printf 'stdout SecretCanary InvalidDataException\n'; printf 'stderr SqliteException SQLite Error 5\n' >&2; } 2>&1 | horse_id_migration_sanitize_startup_logs )
printf '%s\n' "`$combined"
"@
    Assert-Case ($sanitizedBothStreams.ExitCode -eq 0 -and $sanitizedBothStreams.Output -match 'InvalidDataException:1' -and $sanitizedBothStreams.Output -match 'SqliteException:1' -and $sanitizedBothStreams.Output -match 'sqlite_codes=5' -and $sanitizedBothStreams.Output -notmatch 'SecretCanary|SQLite Error') 'Sanitizer must consume both real pipeline streams while emitting only allowlisted categories.'
    $oversizedLogs = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
export HRP_PYTHON3=$(Quote-Bash $pythonBashPath)
if python3 -c 'import sys; sys.stdout.write("x"*262145)' | horse_id_migration_sanitize_startup_logs; then exit 80; else status=`$?; test "`$status" -ne 0; fi
"@
    Assert-Case ($oversizedLogs.ExitCode -eq 0 -and $oversizedLogs.Output -match 'STARTUP_LOGS code=SANITIZER_LIMIT' -and $oversizedLogs.Output -notmatch 'xxxxx') 'Oversized log input must emit a fixed failure category and no content.'

    $logsStubDirectory = Join-Path $tempRoot 'diagnostic-docker-stub'
    [void](New-Item -ItemType Directory -Path $logsStubDirectory)
    $dockerStubPath = Join-Path $logsStubDirectory 'docker'
    [IO.File]::WriteAllText($dockerStubPath, "#!/usr/bin/env bash`nset -eu`nif [[ `"`$1`" == logs ]]; then echo 'stub log failure secret-canary' >&2; exit 42; fi`nexit 91`n")
    $dockerStubBash = ConvertTo-BashPath $dockerStubPath
    $logsStubBash = ConvertTo-BashPath $logsStubDirectory
    [void](Invoke-Bash "chmod +x $(Quote-Bash $dockerStubBash)")
    $failedLogsPipe = Invoke-Bash @"
set -euo pipefail
export PATH=$(Quote-Bash $logsStubBash):`$PATH HRP_PYTHON3=$(Quote-Bash $pythonBashPath)
. $(Quote-Bash $helperBashPath)
logs_status=0
if timeout --signal=TERM --kill-after=1s 2s docker logs --tail 200 container 2>&1 | head -c 262145 | horse_id_migration_sanitize_startup_logs; then :; else logs_status=`$?; echo 'STARTUP_LOGS code=UNAVAILABLE'; fi
test "`$logs_status" -ne 0
"@ 8000
    Assert-Case ($failedLogsPipe.ExitCode -eq 0 -and $failedLogsPipe.Output -match 'STARTUP_LOGS code=UNAVAILABLE' -and $failedLogsPipe.Output -notmatch 'secret-canary|stub log failure') 'Failed docker log retrieval must be a bounded failure, not an empty-log success or raw output.'
    [IO.File]::WriteAllText($dockerStubPath, "#!/usr/bin/env bash`nset -eu`nif [[ `"`$1`" == logs ]]; then sleep 10; fi`nexit 0`n")
    [void](Invoke-Bash "chmod +x $(Quote-Bash $dockerStubBash)")
    $timedOutLogsPipe = Invoke-Bash @"
set -euo pipefail
export PATH=$(Quote-Bash $logsStubBash):`$PATH HRP_PYTHON3=$(Quote-Bash $pythonBashPath)
. $(Quote-Bash $helperBashPath)
logs_status=0
if timeout --signal=TERM --kill-after=1s 1s docker logs --tail 200 container 2>&1 | head -c 262145 | horse_id_migration_sanitize_startup_logs; then :; else logs_status=`$?; echo 'STARTUP_LOGS code=UNAVAILABLE'; fi
test "`$logs_status" -ne 0
"@ 8000
    Assert-Case ($timedOutLogsPipe.ExitCode -eq 0 -and $timedOutLogsPipe.Output -match 'STARTUP_LOGS code=UNAVAILABLE' -and $timedOutLogsPipe.Output -notmatch 'STARTUP_LOGS exceptions=none.*\n?$') 'Timed-out docker log retrieval must fail closed instead of appearing as empty successful logs.'

    if (-not $IsWindows) {
        $ownershipRoot = Join-Path $tempRoot 'ownership-locks'
        [void](New-Item -ItemType Directory -Path $ownershipRoot)
        $sourcePath = Join-Path $ownershipRoot 'fixture.db'
        [IO.File]::WriteAllBytes($sourcePath, [byte[]]::new(0))
        $logicalPath = '/data/fixture.db'
        $runtimeGate = Join-Path $ownershipRoot '.fixture.db.horse-identity-migration.lock'
        $controlGate = Join-Path $ownershipRoot '.fixture.db.horse-identity-migration.supervisor-control.lock'
        $legacyOwner = Join-Path $ownershipRoot '.fixture.db.horse-identity-migration.supervisor-control.owner'
        $bootstrapOwner = Join-Path $ownershipRoot '.fixture.db.horse-identity-migration.supervisor-control.bootstrap.owner'
        Write-OwnershipMarkers $sourcePath $logicalPath $runtimeGate $controlGate
        $sourceBash = ConvertTo-BashPath $sourcePath
        $journalDatabase = Join-Path $ownershipRoot 'horse-identity-migration-journal.db'
        [IO.File]::WriteAllBytes($journalDatabase, [byte[]]::new(0))
        $journalLock = Join-Path $ownershipRoot 'horse-identity-migration-operation.lock'
        [IO.File]::WriteAllBytes($journalLock, [byte[]]::new(0))
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $true)

        foreach ($purpose in @('api-bootstrap', 'cli-operation', 'readonly-reverify')) {
            Write-SupervisorOwnerMarker $legacyOwner $purpose $logicalPath
            [void](Invoke-SourceOwnership $sourceBash $logicalPath $true)
        }
        Write-SupervisorOwnerMarker $bootstrapOwner 'api-bootstrap' $logicalPath
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $true)
        foreach ($purpose in @('cli-operation', 'readonly-reverify')) {
            Write-SupervisorOwnerMarker $bootstrapOwner $purpose $logicalPath
            [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        }
        Write-SupervisorOwnerMarker $bootstrapOwner 'api-bootstrap' $logicalPath
        $bootstrapOriginal = [IO.File]::ReadAllBytes($bootstrapOwner)
        $bootstrapWrongSource = [Text.Encoding]::ASCII.GetString($bootstrapOriginal).Replace(
            [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($logicalPath))), ('D' * 64))
        [IO.File]::WriteAllText($bootstrapOwner, $bootstrapWrongSource, [Text.Encoding]::ASCII)
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        [IO.File]::WriteAllBytes($bootstrapOwner, $bootstrapOriginal)
        [IO.File]::WriteAllText($bootstrapOwner, ([Text.Encoding]::ASCII.GetString($bootstrapOriginal).Replace("v1`n", "v2`n")), [Text.Encoding]::ASCII)
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        $bootstrapInvalidPidFields = [Text.Encoding]::ASCII.GetString($bootstrapOriginal).Split("`n")
        $bootstrapInvalidPidFields[5] = '0'
        [IO.File]::WriteAllText($bootstrapOwner, ($bootstrapInvalidPidFields -join "`n"), [Text.Encoding]::ASCII)
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        [IO.File]::WriteAllBytes($bootstrapOwner, [byte[]]::new(1025))
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        [IO.File]::WriteAllBytes($bootstrapOwner, $bootstrapOriginal)
        $bootstrapOwnerBash = ConvertTo-BashPath $bootstrapOwner
        $bootstrapAlias = "$bootstrapOwner.alias"
        $bootstrapAliasBash = ConvertTo-BashPath $bootstrapAlias
        $bootstrapSymlink = Invoke-Bash "mv -- $(Quote-Bash $bootstrapOwnerBash) $(Quote-Bash $bootstrapAliasBash); ln -s -- $(Quote-Bash $bootstrapAliasBash) $(Quote-Bash $bootstrapOwnerBash)"
        Assert-Case ($bootstrapSymlink.ExitCode -eq 0) 'Could not create the bootstrap supervisor owner symlink fixture.'
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        [void](Invoke-Bash "rm -- $(Quote-Bash $bootstrapOwnerBash); mv -- $(Quote-Bash $bootstrapAliasBash) $(Quote-Bash $bootstrapOwnerBash)")
        $bootstrapHardlink = Invoke-Bash "ln -- $(Quote-Bash $bootstrapOwnerBash) $(Quote-Bash $bootstrapAliasBash)"
        Assert-Case ($bootstrapHardlink.ExitCode -eq 0) 'Could not create the bootstrap supervisor owner hard-link fixture.'
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        [void](Invoke-Bash "rm -- $(Quote-Bash $bootstrapAliasBash)")
        [IO.File]::Delete($bootstrapOwner)
        $legacyOriginal = [IO.File]::ReadAllBytes($legacyOwner)
        $legacyWrongSource = [Text.Encoding]::ASCII.GetString($legacyOriginal).Replace(
            [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($logicalPath))), ('D' * 64))
        [IO.File]::WriteAllText($legacyOwner, $legacyWrongSource, [Text.Encoding]::ASCII)
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        [IO.File]::WriteAllBytes($legacyOwner, $legacyOriginal)
        $legacyWrongFields = [Text.Encoding]::ASCII.GetString($legacyOriginal).Replace("v1`n", "v2`n")
        [IO.File]::WriteAllText($legacyOwner, $legacyWrongFields, [Text.Encoding]::ASCII)
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        $legacyInvalidPidFields = [Text.Encoding]::ASCII.GetString($legacyOriginal).Split("`n")
        $legacyInvalidPidFields[5] = '0'
        [IO.File]::WriteAllText($legacyOwner, ($legacyInvalidPidFields -join "`n"), [Text.Encoding]::ASCII)
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        [IO.File]::WriteAllBytes($legacyOwner, [byte[]]::new(1025))
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        [IO.File]::WriteAllBytes($legacyOwner, $legacyOriginal)
        $legacyOwnerBash = ConvertTo-BashPath $legacyOwner
        $ownerAlias = "$legacyOwner.alias"
        $ownerAliasBash = ConvertTo-BashPath $ownerAlias
        $ownerSymlink = Invoke-Bash "mv -- $(Quote-Bash $legacyOwnerBash) $(Quote-Bash $ownerAliasBash); ln -s -- $(Quote-Bash $ownerAliasBash) $(Quote-Bash $legacyOwnerBash)"
        Assert-Case ($ownerSymlink.ExitCode -eq 0) 'Could not create the legacy supervisor owner symlink fixture.'
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        [void](Invoke-Bash "rm -- $(Quote-Bash $legacyOwnerBash); mv -- $(Quote-Bash $ownerAliasBash) $(Quote-Bash $legacyOwnerBash)")
        $ownerHardlink = Invoke-Bash "ln -- $(Quote-Bash $legacyOwnerBash) $(Quote-Bash $ownerAliasBash)"
        Assert-Case ($ownerHardlink.ExitCode -eq 0) 'Could not create the legacy supervisor owner hard-link fixture.'
        [void](Invoke-SourceOwnership $sourceBash $logicalPath $false)
        [void](Invoke-Bash "rm -- $(Quote-Bash $ownerAliasBash)")
        $bootstrapAliasSource = Join-Path $ownershipRoot '.fixture.db.horse-identity-migration.supervisor-control.bootstrap.owner'
        [IO.File]::WriteAllBytes($bootstrapAliasSource, $bootstrapOriginal)
        $sourceAliasLogical = '/data/.fixture.db.horse-identity-migration.supervisor-control.bootstrap.owner'
        $aliasBefore = (Get-FileHash -LiteralPath $bootstrapAliasSource -Algorithm SHA256).Hash
        $aliasBash = ConvertTo-BashPath $bootstrapAliasSource
        $aliasSourceResult = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
export HRP_PYTHON3=$(Quote-Bash $pythonBashPath)
if horse_id_migration_acquire_source_ownership $(Quote-Bash $sourceBash) $(Quote-Bash $logicalPath) $(Quote-Bash $aliasBash) $(Quote-Bash $sourceAliasLogical); then
  horse_id_migration_release_source_ownership
  exit 80
fi
"@
        Assert-Case ($aliasSourceResult.ExitCode -eq 0 -and $aliasSourceResult.Output -match 'Configured sources and supervisor owner sidecars must not alias') "Configured source and bootstrap owner alias must be rejected in preflight. exit=$($aliasSourceResult.ExitCode) output=$($aliasSourceResult.Output)"
        Assert-Case ($aliasBefore -eq (Get-FileHash -LiteralPath $bootstrapAliasSource -Algorithm SHA256).Hash) 'Alias preflight must not mutate the owner marker.'
        [IO.File]::Delete($bootstrapAliasSource)
        [IO.File]::Delete($legacyOwner)

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
    Assert-Case ($helper -match 'deadline = time\.monotonic\(\) \+ 120[\s\S]*time\.sleep\(1\)[\s\S]*login = urlencode') 'Operator readiness must use a bounded read-only retry window before the single login/UI read.'

    # Exercise the new operator binding with a maintenance gate emitted by the
    # canonical C# encoder, then cover wrong GUID, gate binding, active writer,
    # and readiness failures through parsed Bash rather than string-only checks.
    $operatorData = Join-Path $tempRoot 'operator-binding-fixture'
    [void](New-Item -ItemType Directory -Path $operatorData)
    $requiredInteropFiles = @(
        'eventstore.db', 'collection-platform.db', 'prediction-executions.db',
        'horse-identity-migration-journal.db', 'horse-identity-migration-initialized.marker',
        'horse-identity-migration-operation.lock', 'operator-source-paths.json',
        '.eventstore.db.horse-identity-migration.lock',
        '.collection-platform.db.horse-identity-migration.lock',
        '.prediction-executions.db.horse-identity-migration.lock',
        '.eventstore.db.horse-identity-migration.supervisor-control.lock',
        '.collection-platform.db.horse-identity-migration.supervisor-control.lock',
        '.prediction-executions.db.horse-identity-migration.supervisor-control.lock',
        '.eventstore.db.horse-identity-migration.supervisor-control.bootstrap.owner',
        '.collection-platform.db.horse-identity-migration.supervisor-control.bootstrap.owner',
        '.prediction-executions.db.horse-identity-migration.supervisor-control.bootstrap.owner',
        '.eventstore.db.horse-identity-migration.maintenance',
        '.collection-platform.db.horse-identity-migration.maintenance',
        '.prediction-executions.db.horse-identity-migration.maintenance')
    foreach ($requiredFile in $requiredInteropFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $operatorFixtureDirectory $requiredFile) -PathType Leaf)) {
            throw "The typed operator fixture is missing required file '$requiredFile'."
        }
    }
    foreach ($fixtureFile in Get-ChildItem -Force -LiteralPath $operatorFixtureDirectory) {
        Copy-Item -LiteralPath $fixtureFile.FullName -Destination $operatorData -Recurse
    }
    $operatorGuid = '00000000-0000-0000-0000-000000000001'
    $operatorSourcePaths = @(
        (Join-Path $operatorData 'eventstore.db'),
        (Join-Path $operatorData 'collection-platform.db'),
        (Join-Path $operatorData 'prediction-executions.db'))
    Assert-Case (($operatorSourcePaths | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) }).Count -eq 0) 'The typed operator fixture must include all three configured source databases.'
    $sourcePathMetadata = Get-Item -LiteralPath (Join-Path $operatorFixtureDirectory 'operator-source-paths.json')
    Assert-Case ($sourcePathMetadata.Length -le 4096) 'Typed producer source-path metadata must remain bounded.'
    $producerSourcePaths = @(Get-Content -Raw -LiteralPath $sourcePathMetadata.FullName | ConvertFrom-Json)
    $expectedSourceNames = @('collection-platform.db', 'eventstore.db', 'prediction-executions.db')
    Assert-Case ($producerSourcePaths.Count -eq $expectedSourceNames.Count) 'Typed producer metadata must describe exactly the three real source paths.'
    $producerPathByName = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($producerSourcePath in $producerSourcePaths) {
        if ($producerSourcePath -isnot [string] -or -not [IO.Path]::IsPathFullyQualified($producerSourcePath)) {
            throw 'Typed producer source-path metadata must contain fully qualified paths.'
        }
        $canonicalProducerPath = [IO.Path]::GetFullPath($producerSourcePath)
        $sourceName = [IO.Path]::GetFileName($canonicalProducerPath)
        if ((-not [StringComparer]::Ordinal.Equals($producerSourcePath, $canonicalProducerPath)) -or (-not [StringComparer]::Ordinal.Equals([IO.Path]::GetDirectoryName($canonicalProducerPath), $operatorFixtureDirectory)) -or ($sourceName -notin $expectedSourceNames) -or (-not (Test-Path -LiteralPath $canonicalProducerPath -PathType Leaf)) -or $producerPathByName.ContainsKey($sourceName)) {
            throw 'Typed producer source-path metadata did not match its original fixture files.'
        }
        $producerPathByName.Add($sourceName, $canonicalProducerPath)
    }
    Assert-Case ($expectedSourceNames.Where({ -not $producerPathByName.ContainsKey($_) }).Count -eq 0) 'Typed producer metadata must include every expected source exactly once.'
    $logicalSources = [string[]] @($producerSourcePaths)
    [Array]::Sort($logicalSources, [StringComparer]::Ordinal)
    $operatorSourcePairsBash = ($logicalSources | ForEach-Object {
        $sourceName = [IO.Path]::GetFileName($_)
        (Quote-Bash (ConvertTo-BashPath (Join-Path $operatorData $sourceName))) + ' ' + (Quote-Bash (ConvertTo-BashPath $_))
    }) -join ' '
    $typedOwnershipPaths = @($operatorSourcePaths)
    foreach ($name in @('eventstore.db', 'collection-platform.db', 'prediction-executions.db')) {
        foreach ($suffix in @('.horse-identity-migration.lock',
                '.horse-identity-migration.supervisor-control.lock',
                '.horse-identity-migration.supervisor-control.bootstrap.owner',
                '.horse-identity-migration.maintenance')) {
            $typedOwnershipPaths += Join-Path $operatorData ('.' + $name + $suffix)
        }
    }
    $typedOwnershipPaths += @((Join-Path $operatorData 'horse-identity-migration-journal.db'),
        (Join-Path $operatorData 'horse-identity-migration-initialized.marker'),
        (Join-Path $operatorData 'horse-identity-migration-operation.lock'))
    $typedOwnershipPaths = @($typedOwnershipPaths | Sort-Object -Unique)
    $typedOwnershipHashesBefore = ($typedOwnershipPaths | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }) -join '|'
    $typedOwnershipCheck = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
export HRP_PYTHON3=$(Quote-Bash $pythonBashPath)
horse_id_migration_acquire_source_ownership --preserve-existing-maintenance-gates $operatorSourcePairsBash
horse_id_migration_release_source_ownership
"@ 15000
    Assert-Case ($typedOwnershipCheck.ExitCode -eq 0) "Actual typed C# bootstrap owner markers must interoperate with source ownership validation. $($typedOwnershipCheck.Output)"
    $typedOwnershipHashesAfter = ($typedOwnershipPaths | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }) -join '|'
    Assert-Case ($typedOwnershipHashesBefore -eq $typedOwnershipHashesAfter) 'Typed owner validation must leave source, lock, owner, marker, journal, and gate bytes unchanged.'
    $operatorSource = $operatorSourcePaths[0]
    $operatorJournalPath = Join-Path $operatorData 'horse-identity-migration-journal.db'
    $operatorDataBash = ConvertTo-BashPath $operatorData
    $operatorSourceBash = ConvertTo-BashPath $operatorSource
    $operatorGateRecord = Get-Content -Raw -LiteralPath (Join-Path $operatorData '.eventstore.db.horse-identity-migration.maintenance')
    $operatorRequestHash = & $pythonCommand -c "import sqlite3,sys; print(sqlite3.connect(sys.argv[1]).execute('select request_hash from migration_operations where operation_id=?',(sys.argv[2],)).fetchone()[0])" $operatorJournalPath $operatorGuid
    Assert-Case ($LASTEXITCODE -eq 0 -and $operatorRequestHash -match '^[0-9A-F]{64}$') 'The typed PendingV2 operation request hash must be present.'
    $validBinding = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
horse_id_migration_validate_operator_binding $(Quote-Bash $operatorDataBash) $(Quote-Bash $operatorGuid) $operatorSourcePairsBash
"@ 15000
    Assert-Case ($validBinding.ExitCode -eq 0 -and $validBinding.Output -match 'PASS code=OPERATOR_BINDING') 'Canonical C# gate must interoperate with the read-only operator binding validator.'
    & $pythonCommand -c "import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute('update migration_operations set request_hash=? where operation_id=?',('A'*64,sys.argv[2])); c.commit(); c.close()" $operatorJournalPath $operatorGuid
    $requestHashMismatch = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
if horse_id_migration_validate_operator_binding $(Quote-Bash $operatorDataBash) $(Quote-Bash $operatorGuid) $operatorSourcePairsBash; then exit 80; fi
"@ 15000
    Assert-Case ($requestHashMismatch.ExitCode -eq 0) 'A request JSON hash mismatch must be rejected independently of the immutable intent hash.'
    & $pythonCommand -c "import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute('update migration_operations set request_hash=? where operation_id=?',(sys.argv[3],sys.argv[2])); c.commit(); c.close()" $operatorJournalPath $operatorGuid $operatorRequestHash
    $wrongGuid = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
if horse_id_migration_validate_operator_binding $(Quote-Bash $operatorDataBash) 00000000-0000-0000-0000-000000000002 $operatorSourcePairsBash; then exit 80; fi
"@ 15000
    Assert-Case ($wrongGuid.ExitCode -eq 0) 'Wrong operator GUID must be rejected before mutation.'
    $gateBefore = (Get-FileHash -LiteralPath (Join-Path $operatorData '.eventstore.db.horse-identity-migration.maintenance') -Algorithm SHA256).Hash
    [IO.File]::WriteAllText((Join-Path $operatorData '.eventstore.db.horse-identity-migration.maintenance'), $operatorGateRecord.Replace($operatorGuid, '00000000-0000-0000-0000-000000000002'), [Text.Encoding]::ASCII)
    $bindingMismatch = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
if horse_id_migration_validate_operator_binding $(Quote-Bash $operatorDataBash) $(Quote-Bash $operatorGuid) $operatorSourcePairsBash; then exit 80; fi
"@ 15000
    Assert-Case ($bindingMismatch.ExitCode -eq 0) 'Mismatched maintenance gate binding must be rejected.'
    [IO.File]::WriteAllText((Join-Path $operatorData '.eventstore.db.horse-identity-migration.maintenance'), $operatorGateRecord, [Text.Encoding]::ASCII)
    Assert-Case ($gateBefore -eq (Get-FileHash -LiteralPath (Join-Path $operatorData '.eventstore.db.horse-identity-migration.maintenance') -Algorithm SHA256).Hash) 'Restored canonical gate bytes changed unexpectedly.'
    $logicalSource = '/data/eventstore.db'
    $logicalHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($logicalSource)))
    $sourceGate = Join-Path $operatorData '.eventstore.db.horse-identity-migration.lock'
    $controlGate = Join-Path $operatorData '.eventstore.db.horse-identity-migration.supervisor-control.lock'
    [IO.File]::WriteAllText($sourceGate, "HorseIdentityMigrationSourceOwnership:v1:$logicalHash`n", [Text.Encoding]::ASCII)
    [IO.File]::WriteAllText($controlGate, "HorseIdentityMigrationSupervisorControl:v1:$logicalHash`n", [Text.Encoding]::ASCII)
    $activeWriter = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
exec 9<$(Quote-Bash (ConvertTo-BashPath $controlGate))
flock -n 9
if horse_id_migration_acquire_source_ownership --preserve-existing-maintenance-gates $(Quote-Bash $operatorSourceBash) $(Quote-Bash $logicalSource); then exit 80; fi
"@ 15000
    Assert-Case ($activeWriter.ExitCode -eq 0) 'Active supervisor writer must block operator source ownership.'
    $operatorCollection = Join-Path $operatorData 'collection-platform.db'
    $persistedPause = & $pythonCommand -c "import sqlite3,sys; row=sqlite3.connect(sys.argv[1]).execute('select IsPaused,UpdatedAt from collection_platform_controls where ControlId=?',('pipeline',)).fetchone(); print('%s|%s' % row) if row else sys.exit(2)" $operatorCollection
    Assert-Case ($LASTEXITCODE -eq 0 -and $persistedPause -match '^1\|\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}(?:\.\d{1,7})?$') 'The actual CollectionPlatformStore producer must persist a paused pipeline with its native timestamp representation.'
    $persistedPauseTimestamp = $persistedPause.Substring(2)
    $actualPause = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
horse_id_migration_assert_recovery_collection_state $(Quote-Bash (ConvertTo-BashPath $operatorCollection)) 900
"@ 15000
    Assert-Case ($actualPause.ExitCode -eq 0 -and $actualPause.Output -match 'PASS code=RECOVERY_QUIESCENT') 'The native producer pause timestamp must satisfy the actual recovery quiescence parser.'
    & $pythonCommand -c "import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute('update collection_platform_controls set IsPaused=0 where ControlId=?',('pipeline',)); c.commit(); c.close()" $operatorCollection
    $readinessFailure = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
if horse_id_migration_assert_recovery_collection_state $(Quote-Bash (ConvertTo-BashPath $operatorCollection)) 900; then exit 80; fi
"@ 15000
    Assert-Case ($readinessFailure.ExitCode -eq 0) 'Unpaused collection state must block operator bootstrap.'
    & $pythonCommand -c "import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute('update collection_platform_controls set IsPaused=1,UpdatedAt=? where ControlId=?',(sys.argv[2],'pipeline')); c.commit(); c.close()" $operatorCollection 'not-a-timestamp'
    $malformedPause = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
if horse_id_migration_assert_recovery_collection_state $(Quote-Bash (ConvertTo-BashPath $operatorCollection)) 900; then exit 80; fi
"@ 15000
    Assert-Case ($malformedPause.ExitCode -eq 0) 'Malformed non-production pause timestamps must be rejected.'
    & $pythonCommand -c "import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute('update collection_platform_controls set UpdatedAt=? where ControlId=?',('2999-01-01 00:00:00.0000000','pipeline')); c.commit(); c.close()" $operatorCollection
    $futurePause = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
if horse_id_migration_assert_recovery_collection_state $(Quote-Bash (ConvertTo-BashPath $operatorCollection)) 900; then exit 80; fi
"@ 15000
    Assert-Case ($futurePause.ExitCode -eq 0) 'Future JST pause timestamps must be rejected.'
    & $pythonCommand -c "import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute('update collection_platform_controls set UpdatedAt=? where ControlId=?',(sys.argv[2],'pipeline')); c.commit(); c.close()" $operatorCollection $persistedPauseTimestamp
    $stalePause = Invoke-Bash @"
set -euo pipefail
. $(Quote-Bash $helperBashPath)
horse_id_migration_assert_recovery_collection_state $(Quote-Bash (ConvertTo-BashPath $operatorCollection)) 900
"@ 15000
    Assert-Case ($stalePause.ExitCode -eq 0 -and $stalePause.Output -match 'PASS code=RECOVERY_QUIESCENT') 'A canonical stale JST pause must pass the bounded offline drain check.'

    Write-Output 'PASS T4 deploy guard fixtures, all-store integrity, bounds, backup sidecars, capability, child timeout and workflow order'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}
