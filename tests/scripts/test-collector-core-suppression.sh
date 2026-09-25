#!/bin/sh
set -eu

bootstrap="${1:-/var/runtime/bootstrap}"
helper="${2:-/var/runtime/collector-temp-lifecycle.sh}"
session_command="${COLLECTOR_SESSION_COMMAND:-/usr/bin/setsid}"

[ -r "$bootstrap" ]
[ -r "$helper" ]
[ -x "$session_command" ]

if command -v python3 >/dev/null 2>&1; then
  abort_runtime=python3
elif [ -x /var/task/.playwright/node/linux-x64/node ]; then
  abort_runtime=/var/task/.playwright/node/linux-x64/node
else
  echo 'An abort-capable runtime is required.' >&2
  exit 1
fi

probe_root=$(mktemp -d)
case "$probe_root" in
  /tmp/tmp.*) ;;
  *) echo 'Unsafe probe directory.' >&2; exit 1 ;;
esac
cleanup() {
  find "$probe_root" -maxdepth 2 -type f -name 'core*' -delete 2>/dev/null || true
  rm -rf "$probe_root"
}
trap cleanup EXIT HUP INT TERM

fake="$probe_root/abort-collector"
trace="$probe_root/collector-core-limit"
diagnostic="$probe_root/diagnostic"
cat >"$fake" <<'EOF'
#!/bin/sh
ulimit -c >"$COLLECTOR_CORE_LIMIT_TRACE"
case "$COLLECTOR_ABORT_RUNTIME" in
  python3) exec python3 -c 'import os; os.abort()' ;;
  *) exec "$COLLECTOR_ABORT_RUNTIME" -e 'process.abort()' ;;
esac
EOF
chmod 0755 "$fake"

parent_core_limit=$(ulimit -c)
mkdir "$probe_root/collector-cwd"
cd "$probe_root/collector-cwd"
export COLLECTOR_TEMP_ROOT="$probe_root/owned"
export COLLECTOR_TEMP_HELPER="$helper"
export COLLECTOR_EXECUTABLE="$fake"
export COLLECTOR_SESSION_COMMAND="$session_command"
export COLLECTOR_CORE_LIMIT_TRACE="$trace"
export COLLECTOR_ABORT_RUNTIME="$abort_runtime"
unset AWS_LAMBDA_RUNTIME_API

collector_status=0
sh "$bootstrap" >/dev/null 2>"$diagnostic" || collector_status=$?
collector_core_limit=$(cat "$trace" 2>/dev/null || true)
collector_core_count=$(find . -maxdepth 1 -type f -name 'core*' | wc -l)
printf 'CollectorStatus=%s CollectorCoreLimit=%s CollectorCoreFiles=%s ParentCoreLimitBefore=%s ParentCoreLimitAfter=%s\n' \
  "$collector_status" "$collector_core_limit" "$collector_core_count" "$parent_core_limit" "$(ulimit -c)"
[ "$collector_status" -eq 134 ]
[ "$collector_core_limit" = 0 ]
[ "$(ulimit -c)" = "$parent_core_limit" ]
[ "$collector_core_count" -eq 0 ]
grep -q 'Phase=post-group-termination .*GroupAlive=false' "$diagnostic"
grep -q 'Phase=post-delete InvocationKiB=0 ' "$diagnostic"
grep -Eq 'Collector descendant diagnostic\. Phase=post-group-termination Captured=[0-9]+ Alive=0 OutsideGroup=0 DeletedFds=0' "$diagnostic"
[ -z "$(find "$COLLECTOR_TEMP_ROOT" -mindepth 1 -maxdepth 1 -type d -name 'invocation.*' -print -quit)" ]

mkdir "$probe_root/control-cwd"
cd "$probe_root/control-cwd"
control_status=0
(
  ulimit -c unlimited
  case "$abort_runtime" in
    python3) exec python3 -c 'import os; os.abort()' ;;
    *) exec "$abort_runtime" -e 'process.abort()' ;;
  esac
) >/dev/null 2>&1 || control_status=$?
control_core_count=$(find . -maxdepth 1 -type f -name 'core*' | wc -l)
printf 'ControlStatus=%s ControlCoreFiles=%s ParentCoreLimitAfterControl=%s CorePattern=%s\n' \
  "$control_status" "$control_core_count" "$(ulimit -c)" "$(cat /proc/sys/kernel/core_pattern)"
[ "$control_status" -eq 134 ]
[ "$(ulimit -c)" = "$parent_core_limit" ]

core_pattern=$(cat /proc/sys/kernel/core_pattern)
case "$core_pattern" in
  core|core.*)
    [ "$control_core_count" -gt 0 ]
    ;;
esac

echo 'PASS collector-child-core-suppression'
