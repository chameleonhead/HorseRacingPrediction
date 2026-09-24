param([string]$Bash = 'C:\Program Files\Git\bin\bash.exe')
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$helper = (Resolve-Path (Join-Path $repositoryRoot 'deploy/collector-temp-lifecycle.sh')).Path
$bootstrap = (Resolve-Path (Join-Path $repositoryRoot 'deploy/lambda-bootstrap')).Path
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("hrp-collector-temp-test-" + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testRoot

function Convert-ToBashPath([string]$Path) {
    $resolved = [System.IO.Path]::GetFullPath($Path)
    if ($resolved -match '^([A-Za-z]):\\(.*)$') {
        return "/$($Matches[1].ToLowerInvariant())/$($Matches[2] -replace '\\', '/')"
    }
    return $resolved -replace '\\', '/'
}

function Invoke-BashCase([string]$Name, [string]$Script) {
    $output = $Script | & $Bash --noprofile --norc -s 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "FAIL $Name ($LASTEXITCODE): $($output -join "`n")"
    }
    Write-Output "PASS $Name"
}

try {
    $root = Convert-ToBashPath $testRoot
    $helperPath = Convert-ToBashPath $helper
    $bootstrapPath = Convert-ToBashPath $bootstrap

    Invoke-BashCase 'normal-cleanup-and-isolation' @"
set -eu
export COLLECTOR_TEMP_ROOT='$root/normal'
. '$helperPath'
collector_temp_initialize
touch '$root/unrelated'
invocation=`$(collector_temp_begin_invocation)
[ -d "`$invocation/home" ] && [ -d "`$invocation/tmp" ]
collector_temp_finish_invocation "`$invocation"
[ ! -e "`$invocation" ]
[ -f '$root/unrelated' ]
"@

    Invoke-BashCase 'stale-owned-cleanup' @"
set -eu
export COLLECTOR_TEMP_ROOT='$root/stale'
. '$helperPath'
mkdir -p "`$COLLECTOR_TEMP_ROOT/invocation.stale"
printf '%s\n' "`$COLLECTOR_TEMP_MARKER" >"`$COLLECTOR_TEMP_ROOT/invocation.stale/.hrp-collector-owned"
printf '999999 1\n' >"`$COLLECTOR_TEMP_ROOT/invocation.stale/.active-pid"
collector_temp_initialize
[ ! -e "`$COLLECTOR_TEMP_ROOT/invocation.stale" ]
"@

    Invoke-BashCase 'enospc-create-fails-closed' @"
set -eu
bin='$root/enospc-bin'
mkdir -p "`$bin"
cat >"`$bin/mktemp" <<'EOF'
#!/bin/sh
echo 'mktemp: No space left on device' >&2
exit 1
EOF
chmod +x "`$bin/mktemp"
export COLLECTOR_TEMP_ROOT='$root/enospc'
. '$helperPath'
collector_temp_initialize
touch '$root/enospc-unrelated'
PATH="`$bin:`$PATH"
if collector_temp_begin_invocation >/dev/null 2>&1; then exit 83; fi
[ -f '$root/enospc-unrelated' ]
[ -z "`$(find "`$COLLECTOR_TEMP_ROOT" -mindepth 1 -maxdepth 1 -type d -name 'invocation.*' -print -quit)" ]
"@

    Invoke-BashCase 'active-and-unowned-preserved' @"
set -eu
export COLLECTOR_TEMP_ROOT='$root/preserve'
. '$helperPath'
mkdir -p "`$COLLECTOR_TEMP_ROOT/invocation.active" "`$COLLECTOR_TEMP_ROOT/invocation.unowned"
printf '%s\n' "`$COLLECTOR_TEMP_MARKER" >"`$COLLECTOR_TEMP_ROOT/invocation.active/.hrp-collector-owned"
process_start=`$(awk '{ print `$22 }' "/proc/`$`$/stat")
printf '%s %s\n' "`$`$" "`$process_start" >"`$COLLECTOR_TEMP_ROOT/invocation.active/.active-pid"
collector_temp_initialize
[ -d "`$COLLECTOR_TEMP_ROOT/invocation.active" ]
[ -d "`$COLLECTOR_TEMP_ROOT/invocation.unowned" ]
"@

    Invoke-BashCase 'pid-reuse-does-not-preserve-stale-lease' @"
set -eu
export COLLECTOR_TEMP_ROOT='$root/pid-reuse'
. '$helperPath'
mkdir -p "`$COLLECTOR_TEMP_ROOT/invocation.reused"
printf '%s\n' "`$COLLECTOR_TEMP_MARKER" >"`$COLLECTOR_TEMP_ROOT/invocation.reused/.hrp-collector-owned"
printf '%s 0\n' "`$`$" >"`$COLLECTOR_TEMP_ROOT/invocation.reused/.active-pid"
collector_temp_initialize
[ ! -e "`$COLLECTOR_TEMP_ROOT/invocation.reused" ]
"@

    Invoke-BashCase 'symlink-and-unsafe-root-rejected' @"
set -eu
export COLLECTOR_TEMP_ROOT='$root/symlinks'
. '$helperPath'
mkdir -p "`$COLLECTOR_TEMP_ROOT" '$root/target'
ln -s '$root/target' "`$COLLECTOR_TEMP_ROOT/invocation.link"
if [ -L "`$COLLECTOR_TEMP_ROOT/invocation.link" ]; then
  collector_temp_cleanup_stale
  [ -L "`$COLLECTOR_TEMP_ROOT/invocation.link" ]
else
  # MSYS may materialize a directory junction instead of a POSIX symlink.
  # The existing Linux CI invocation exercises the real symlink assertion.
  rm -rf "`$COLLECTOR_TEMP_ROOT/invocation.link"
fi
COLLECTOR_TEMP_ROOT=/tmp
if collector_temp_initialize; then exit 81; fi
COLLECTOR_TEMP_ROOT=/
if collector_temp_initialize; then exit 82; fi
"@

    Invoke-BashCase 'bootstrap-success-and-failure-cleanup' @"
set -eu
fake='$root/fake-collector.sh'
cat >"`$fake" <<'EOF'
#!/bin/sh
[ "`$HOME" = "`$COLLECTOR_TEMP_ROOT" ] && exit 90
[ -d "`$HOME" ] && [ -d "`$TMPDIR" ]
exit "`${FAKE_EXIT:-0}"
EOF
chmod +x "`$fake"
for code in 0 7; do
  export COLLECTOR_TEMP_ROOT='$root/bootstrap-'"`$code"
  export COLLECTOR_TEMP_HELPER='$helperPath'
  export COLLECTOR_EXECUTABLE="`$fake"
  export FAKE_EXIT="`$code"
  unset AWS_LAMBDA_RUNTIME_API
  actual=0
  sh '$bootstrapPath' >/dev/null 2>&1 || actual=`$?
  [ "`$actual" -eq "`$code" ]
  [ -z "`$(find "`$COLLECTOR_TEMP_ROOT" -mindepth 1 -maxdepth 1 -type d -name 'invocation.*' -print -quit)" ]
done
"@

    Invoke-BashCase 'lambda-header-correlation-and-cleanup' @"
set -eu
bin='$root/fake-bin'
mkdir -p "`$bin"
cat >"`$bin/curl" <<'EOF'
#!/bin/sh
headers=''
event=''
previous=''
for argument in "`$@"; do
  if [ "`$previous" = '-D' ]; then headers="`$argument"; fi
  if [ "`$previous" = '-o' ]; then event="`$argument"; fi
  previous="`$argument"
done
url=''
for argument in "`$@"; do
  case "`$argument" in http://*) url="`$argument" ;; esac
done
case "`$url" in
  */invocation/next)
    printf 'Lambda-Runtime-Aws-Request-Id: 11111111-2222-3333-4444-555555555555\nLambda-Runtime-Deadline-Ms: 1900000000000\n' >"`$headers"
    printf '{"Records":[]}' >"`$event"
    ;;
  */11111111-2222-3333-4444-555555555555/response)
    printf 'response\n' >>"`$COLLECTOR_TEST_TRACE"
    ;;
  *) exit 91 ;;
esac
EOF
cat >"`$bin/collector" <<'EOF'
#!/bin/sh
[ "`$AWS_LAMBDA_REQUEST_ID" = '11111111-2222-3333-4444-555555555555' ]
[ "`$AWS_LAMBDA_DEADLINE_MS" = '1900000000000' ]
[ -f "`$COLLECTOR_EVENT_PATH" ]
printf '{"batchItemFailures":[]}' >"`$COLLECTOR_RESPONSE_PATH"
EOF
chmod +x "`$bin/curl" "`$bin/collector"
export PATH="`$bin:`$PATH"
export COLLECTOR_TEMP_ROOT='$root/lambda'
export COLLECTOR_TEMP_HELPER='$helperPath'
export COLLECTOR_EXECUTABLE="`$bin/collector"
export COLLECTOR_TEST_TRACE='$root/lambda-trace'
export COLLECTOR_BOOTSTRAP_TEST_ONCE=1
export AWS_LAMBDA_RUNTIME_API=runtime.test
sh '$bootstrapPath' >/dev/null 2>&1
grep -qx response "`$COLLECTOR_TEST_TRACE"
[ -z "`$(find "`$COLLECTOR_TEMP_ROOT" -mindepth 1 -maxdepth 1 -type d -name 'invocation.*' -print -quit)" ]
"@

    Invoke-BashCase 'signal-cleanup' @"
set -eu
fake='$root/signal-collector.sh'
cat >"`$fake" <<'EOF'
#!/bin/sh
touch "`$COLLECTOR_TEST_READY"
sleep 30
EOF
chmod +x "`$fake"
export COLLECTOR_TEMP_ROOT='$root/signal'
export COLLECTOR_TEMP_HELPER='$helperPath'
export COLLECTOR_EXECUTABLE="`$fake"
export COLLECTOR_TEST_READY='$root/signal-ready'
unset AWS_LAMBDA_RUNTIME_API
sh '$bootstrapPath' >/dev/null 2>&1 &
bootstrap_pid=`$!
attempt=0
while [ ! -f "`$COLLECTOR_TEST_READY" ] && [ "`$attempt" -lt 100 ]; do
  sleep 0.05
  attempt=`$((attempt + 1))
done
[ -f "`$COLLECTOR_TEST_READY" ]
kill -TERM "`$bootstrap_pid"
status=0
wait "`$bootstrap_pid" || status=`$?
[ "`$status" -eq 143 ]
[ -z "`$(find "`$COLLECTOR_TEMP_ROOT" -mindepth 1 -maxdepth 1 -type d -name 'invocation.*' -print -quit)" ]
"@
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
