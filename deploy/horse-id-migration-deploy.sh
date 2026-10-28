#!/usr/bin/env bash
# Sourceable deployment helpers for the fixed horse-identity migration workflow.
# This file intentionally has no top-level side effects.

HRP_MIGRATION_MAX_JOURNAL_BYTES=$((64 * 1024 * 1024))
HRP_MIGRATION_CLI_TIMEOUT_SECONDS=${HRP_MIGRATION_CLI_TIMEOUT_SECONDS:-$((30 * 60))}
HRP_MIGRATION_CHILD_GRACE_SECONDS=${HRP_MIGRATION_CHILD_GRACE_SECONDS:-10}
HRP_MIGRATION_CONTAINER_NAME=
HRP_MIGRATION_SOURCE_LOCK_FDS=()

horse_id_migration_validate_guid() {
  local operation_id=${1-}
  [[ "$operation_id" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]] &&
    [[ "${operation_id//-/}" != "00000000000000000000000000000000" ]]
}

horse_id_migration_write_operator_override() {
  local override_path=${1-}
  local operation_id=${2-}
  horse_id_migration_validate_guid "$operation_id" || return 1
  [[ -n "$override_path" && ! -e "$override_path" && ! -L "$override_path" ]] || return 1
  (
    umask 077
    set -o noclobber
    printf '%s\n' \
      'services:' \
      '  api:' \
      '    environment:' \
      '      HRP_HORSE_IDENTITY_OPERATOR_ONLY: "true"' \
      "      HRP_HORSE_IDENTITY_OPERATOR_OPERATION_ID: \"$operation_id\"" > "$override_path"
  )
}

horse_id_migration_require_tools() {
  local python_command=${HRP_PYTHON3:-python3}
  command -v "$python_command" >/dev/null 2>&1 || {
    echo "Migration guard requires python3; refusing deployment without installing packages." >&2
    return 1
  }
  command -v timeout >/dev/null 2>&1 || {
    echo "Migration guard requires the existing timeout utility." >&2
    return 1
  }
  command -v docker >/dev/null 2>&1 || {
    echo "Migration guard requires Docker." >&2
    return 1
  }
  for utility in awk chmod cp diff dirname mkdir sha256sum sort cut; do
    command -v "$utility" >/dev/null 2>&1 || {
      echo "Migration guard requires the existing $utility utility." >&2
      return 1
    }
  done
  "$python_command" -c 'import sqlite3' >/dev/null 2>&1 || {
    echo "Migration guard requires Python sqlite3; refusing deployment without installing packages." >&2
    return 1
  }
}

horse_id_migration_require_recovery_tools() {
  local utility
  for utility in flock realpath stat tr wc; do
    command -v "$utility" >/dev/null 2>&1 || {
      echo "API startup recovery requires the existing $utility utility." >&2
      return 1
    }
  done
}

# Hold the application's existing per-source FileShare.None gates while the API
# is stopped. Each pair is a physical host path followed by the exact path seen
# by the API (for the deployed Compose bind these are /opt/.../data/x and
# /data/x). Do not unlink gates: their inode and validated path identity are the
# cross-process protocol shared with HorseIdentityMigrationRuntimeOwnership.
horse_id_migration_validate_supervisor_owner_record() {
  local owner=${1:?supervisor owner path is required}
  local source_set_hash=${2:?source-set hash is required}
  local owner_kind=${3:?owner kind is required}
  if [[ ! -e "$owner" && ! -L "$owner" ]]; then return 0; fi
  if [[ -L "$owner" || ! -f "$owner" || "$(stat -c %h -- "$owner" 2>/dev/null)" != 1 ||
        "$(stat -c %s -- "$owner" 2>/dev/null)" -lt 1 || "$(stat -c %s -- "$owner" 2>/dev/null)" -gt 1024 ||
        "$(realpath -e -- "$owner" 2>/dev/null)" != "$owner" ]]; then
    return 1
  fi
  timeout --signal=TERM --kill-after=5s 60s "${HRP_PYTHON3:-python3}" - "$owner" "$source_set_hash" "$owner_kind" <<'PY'
import os, re, stat, sys
from pathlib import Path

try:
    path = Path(sys.argv[1])
    expected_source_set_hash = sys.argv[2]
    owner_kind = sys.argv[3]
    before = path.lstat()
    if (not stat.S_ISREG(before.st_mode) or before.st_nlink != 1
            or before.st_size < 1 or before.st_size > 1024
            or os.path.realpath(path) != str(path)):
        raise ValueError()
    fd = os.open(path, os.O_RDONLY | getattr(os, 'O_NOFOLLOW', 0) | getattr(os, 'O_CLOEXEC', 0))
    try:
        opened = os.fstat(fd)
        if (opened.st_dev, opened.st_ino, opened.st_size, opened.st_mtime_ns, opened.st_ctime_ns) != (
                before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns, before.st_ctime_ns):
            raise ValueError()
        raw = os.read(fd, 1025)
        after = os.fstat(fd)
        if (len(raw) != before.st_size or
                (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns, after.st_ctime_ns) !=
                (opened.st_dev, opened.st_ino, opened.st_size, opened.st_mtime_ns, opened.st_ctime_ns)):
            raise ValueError()
    finally:
        os.close(fd)
    fields = raw.decode('ascii').split('\n')
    allowed_purposes = (('api-bootstrap',) if owner_kind == 'bootstrap'
                        else ('api-bootstrap', 'cli-operation', 'readonly-reverify') if owner_kind == 'legacy'
                        else ())
    if (len(fields) != 8 or fields[0] != 'v1' or fields[1] not in allowed_purposes
            or not re.fullmatch(r'[0-9A-Fa-f]{64}', fields[4])
            or fields[4] != expected_source_set_hash
            or not re.fullmatch(r'[0-9A-Fa-f]{64}', fields[7])
            or not re.fullmatch(r'[0-9]+', fields[5]) or int(fields[5]) <= 0
            or not re.fullmatch(r'[0-9]+', fields[6]) or int(fields[6]) <= 0):
        raise ValueError()
    if fields[1] == 'api-bootstrap':
        if fields[2:4] != ['-', '-']:
            raise ValueError()
    elif (not re.fullmatch(r'[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}', fields[2])
          or fields[2] == '00000000-0000-0000-0000-000000000000'
          or not re.fullmatch(r'[0-9A-Fa-f]{64}', fields[3])):
        raise ValueError()
except (OSError, UnicodeError, ValueError):
    raise SystemExit(1)
PY
}

horse_id_migration_acquire_source_ownership() {
  local preserve_existing_maintenance_gates=false
  if [[ "${1-}" == "--preserve-existing-maintenance-gates" ]]; then
    preserve_existing_maintenance_gates=true
    shift
  fi
  local physical logical gate expected actual fd source_name control owner bootstrap_owner source_set_hash gate_identity fd_identity
  local -a pairs=("$@")
  local -a logical_paths=()
  local index
  if (( ${#pairs[@]} == 0 || ${#pairs[@]} % 2 != 0 )); then
    echo "Source ownership requires physical/API path pairs." >&2
    return 1
  fi
  if ! timeout --signal=TERM --kill-after=5s 60s "${HRP_PYTHON3:-python3}" - "${pairs[@]}" <<'PY'
import os, sys
from pathlib import Path

try:
    pairs = sys.argv[1:]
    if not pairs or len(pairs) % 2:
        raise ValueError()
    sources = pairs[::2]
    if any(not Path(path).is_absolute() or str(Path(path)) != path for path in sources):
        raise ValueError()
    sidecars = []
    for source in sources:
        path = Path(source)
        sidecars.extend((str(path.parent / ('.' + path.name + '.horse-identity-migration.supervisor-control.owner')),
                         str(path.parent / ('.' + path.name + '.horse-identity-migration.supervisor-control.bootstrap.owner'))))
    identities = [os.path.normcase(os.path.realpath(path)) for path in sources + sidecars]
    if len(set(identities)) != len(identities):
        raise ValueError()
except (OSError, ValueError):
    raise SystemExit(1)
PY
  then
    echo "Configured sources and supervisor owner sidecars must not alias." >&2
    return 1
  fi
  for (( index=1; index<${#pairs[@]}; index+=2 )); do logical_paths+=("${pairs[index]}"); done
  source_set_hash=$(printf '%s\0' "${logical_paths[@]}" | "${HRP_PYTHON3:-python3}" -c \
    'import hashlib,sys; p=[x.decode("utf-8") for x in sys.stdin.buffer.read().split(b"\0") if x]; k=lambda x:x.encode("utf-16-be"); s=sorted(p,key=k); valid=all(x.startswith("/") and not x.endswith("/") and not any(part in (".","..") for part in x.split("/")) and not any(ch.isspace() for ch in x) for x in p); sys.exit(1) if not valid or len(set(p))!=len(p) or p!=s else None; print(hashlib.sha256("\n".join(p).encode("utf-8")).hexdigest().upper())') || {
    echo "Could not compute the configured source identity." >&2
    return 1
  }

  for (( index=0; index<${#pairs[@]}; index+=2 )); do
    physical=${pairs[index]}
    logical=${pairs[index+1]}
    if [[ "$logical" != /* || "$logical" == *"/../"* || "$logical" == */.. ||
          ! -f "$physical" || -L "$physical" || "$(realpath -e -- "$physical" 2>/dev/null)" != "$physical" ||
          "$(stat -c %h -- "$physical" 2>/dev/null)" != 1 ]]; then
      echo "Configured source ownership path is invalid." >&2
      horse_id_migration_release_source_ownership
      return 1
    fi
    source_name=${physical##*/}
    gate="${physical%/*}/.${source_name}.horse-identity-migration.lock"
    expected="HorseIdentityMigrationSourceOwnership:v1:$(printf '%s' "$logical" | sha256sum | cut -d' ' -f1 | tr '[:lower:]' '[:upper:]')"
    if [[ ! -f "$gate" || -L "$gate" || "$(stat -c %h -- "$gate" 2>/dev/null)" != 1 ||
          "$(realpath -e -- "$gate" 2>/dev/null)" != "$gate" ||
          "$(stat -c %s -- "$gate" 2>/dev/null)" != "$(( ${#expected} + 1 ))" ]]; then
      echo "Source ownership gate is not a regular unaliased file." >&2
      horse_id_migration_release_source_ownership
      return 1
    fi
    exec {fd}<"$gate" || {
      echo "Could not open the existing source ownership gate." >&2
      horse_id_migration_release_source_ownership
      return 1
    }
    gate_identity=$(stat -Lc '%d:%i:%h:%s' -- "$gate" 2>/dev/null) || {
      horse_id_migration_release_source_ownership
      return 1
    }
    fd_identity=$(stat -Lc '%d:%i:%h:%s' -- "/proc/$$/fd/$fd" 2>/dev/null) || {
      horse_id_migration_release_source_ownership
      return 1
    }
    if [[ "$gate_identity" != "$fd_identity" ]]; then
      eval "exec ${fd}>&-"
      echo "Source ownership gate identity changed while opening it." >&2
      horse_id_migration_release_source_ownership
      return 1
    fi
    if ! flock -n "$fd"; then
      eval "exec ${fd}>&-"
      echo "A configured source is held by another process." >&2
      horse_id_migration_release_source_ownership
      return 1
    fi
    HRP_MIGRATION_SOURCE_LOCK_FDS+=("$fd")
    if [[ ! -f "$gate" || -L "$gate" || "$(stat -c %h -- "$gate" 2>/dev/null)" != 1 ||
          "$(realpath -e -- "$gate" 2>/dev/null)" != "$gate" ]]; then
      echo "Source ownership gate changed while acquiring it." >&2
      horse_id_migration_release_source_ownership
      return 1
    fi
    gate_identity=$(stat -Lc '%d:%i:%h:%s' -- "$gate" 2>/dev/null) || gate_identity=
    fd_identity=$(stat -Lc '%d:%i:%h:%s' -- "/proc/$$/fd/$fd" 2>/dev/null) || fd_identity=
    if [[ -z "$gate_identity" || "$gate_identity" != "$fd_identity" ]]; then
      echo "Source ownership gate identity changed while acquiring it." >&2
      horse_id_migration_release_source_ownership
      return 1
    fi
    if [[ "$(stat -c %s -- "$gate" 2>/dev/null)" != "$(( ${#expected} + 1 ))" ]]; then
      echo "Source ownership gate size is invalid." >&2
      horse_id_migration_release_source_ownership
      return 1
    fi
    actual=$(<"$gate")
    if [[ "$actual" != "$expected" || "$(wc -l < "$gate")" != 1 ]]; then
      echo "Source ownership gate identity is invalid." >&2
      horse_id_migration_release_source_ownership
      return 1
    fi
    if [[ "$preserve_existing_maintenance_gates" != true &&
          -e "${physical%/*}/.${source_name}.horse-identity-migration.maintenance" ]]; then
      echo "An active migration maintenance gate exists for a configured source." >&2
      horse_id_migration_release_source_ownership
      return 1
    fi

    control="${physical%/*}/.${source_name}.horse-identity-migration.supervisor-control.lock"
    owner="${physical%/*}/.${source_name}.horse-identity-migration.supervisor-control.owner"
    bootstrap_owner="${physical%/*}/.${source_name}.horse-identity-migration.supervisor-control.bootstrap.owner"
    if [[ ! -f "$control" || -L "$control" || "$(stat -c %h -- "$control" 2>/dev/null)" != 1 ||
          "$(realpath -e -- "$control" 2>/dev/null)" != "$control" ]]; then
      echo "Supervisor control lock is not a regular unaliased file." >&2
      horse_id_migration_release_source_ownership
      return 1
    fi
    exec {fd}<"$control" || {
      echo "Could not open the supervisor control lock." >&2
      horse_id_migration_release_source_ownership
      return 1
    }
    if ! flock -n "$fd"; then
      eval "exec ${fd}>&-"
      echo "An active supervisor control owner exists." >&2
      horse_id_migration_release_source_ownership
      return 1
    fi
    HRP_MIGRATION_SOURCE_LOCK_FDS+=("$fd")
    expected="HorseIdentityMigrationSupervisorControl:v1:$(printf '%s' "$logical" | sha256sum | cut -d' ' -f1 | tr '[:lower:]' '[:upper:]')"
    if [[ "$(stat -c %s -- "$control" 2>/dev/null)" != "$(( ${#expected} + 1 ))" ]]; then
      echo "Supervisor control lock size is invalid." >&2
      horse_id_migration_release_source_ownership
      return 1
    fi
    gate_identity=$(stat -Lc '%d:%i:%h:%s' -- "$control" 2>/dev/null) || {
      horse_id_migration_release_source_ownership
      return 1
    }
    fd_identity=$(stat -Lc '%d:%i:%h:%s' -- "/proc/$$/fd/$fd" 2>/dev/null) || {
      horse_id_migration_release_source_ownership
      return 1
    }
    if [[ "$gate_identity" != "$fd_identity" ]]; then
      echo "Supervisor control lock identity changed while opening it." >&2
      horse_id_migration_release_source_ownership
      return 1
    fi
    actual=$(<"$control")
    if [[ "$actual" != "$expected" || "$(wc -l < "$control")" != 1 ]]; then
      echo "Supervisor control lock identity is invalid." >&2
      horse_id_migration_release_source_ownership
      return 1
    fi
    if ! horse_id_migration_validate_supervisor_owner_record "$owner" "$source_set_hash" legacy ||
       ! horse_id_migration_validate_supervisor_owner_record "$bootstrap_owner" "$source_set_hash" bootstrap; then
      echo "Supervisor owner record is active or invalid." >&2
      horse_id_migration_release_source_ownership
      return 1
    fi
  done
}

horse_id_migration_acquire_journal_operation_lock() {
  local journal_path=${1:?journal database path is required}
  local lock_path="${journal_path%/*}/horse-identity-migration-operation.lock"
  local fd
  if [[ ! -f "$lock_path" || -L "$lock_path" || "$(stat -c %h -- "$lock_path" 2>/dev/null)" != 1 ||
        "$(realpath -e -- "$lock_path" 2>/dev/null)" != "$lock_path" || "$(stat -c %s -- "$lock_path" 2>/dev/null)" != 0 ]]; then
    echo "Journal operation lock is not a regular unaliased file." >&2
    return 1
  fi
  exec {fd}<"$lock_path" || return 1
  local path_identity fd_identity
  path_identity=$(stat -Lc '%d:%i:%h:%s' -- "$lock_path" 2>/dev/null) || path_identity=
  fd_identity=$(stat -Lc '%d:%i:%h:%s' -- "/proc/$$/fd/$fd" 2>/dev/null) || fd_identity=
  if [[ -z "$path_identity" || "$path_identity" != "$fd_identity" ]]; then
    eval "exec ${fd}>&-"
    echo "Journal operation lock identity changed while opening it." >&2
    return 1
  fi
  if ! flock -n "$fd"; then
    eval "exec ${fd}>&-"
    echo "An active journal operation owner exists." >&2
    return 1
  fi
  path_identity=$(stat -Lc '%d:%i:%h:%s' -- "$lock_path" 2>/dev/null) || path_identity=
  fd_identity=$(stat -Lc '%d:%i:%h:%s' -- "/proc/$$/fd/$fd" 2>/dev/null) || fd_identity=
  if [[ -z "$path_identity" || "$path_identity" != "$fd_identity" ]]; then
    eval "exec ${fd}>&-"
    echo "Journal operation lock identity changed while acquiring it." >&2
    return 1
  fi
  HRP_MIGRATION_SOURCE_LOCK_FDS+=("$fd")
}

horse_id_migration_release_source_ownership() {
  local index fd
  for (( index=${#HRP_MIGRATION_SOURCE_LOCK_FDS[@]}-1; index>=0; index-- )); do
    fd=${HRP_MIGRATION_SOURCE_LOCK_FDS[index]}
    flock -u "$fd" 2>/dev/null || true
    eval "exec ${fd}>&-"
  done
  HRP_MIGRATION_SOURCE_LOCK_FDS=()
}

horse_id_migration_check_image_capability() {
  local image=${1:?image is required}
  local capability
  capability=$(docker image inspect --format '{{ index .Config.Labels "com.horseracingprediction.identity-migration-guard" }}' "$image") || {
    echo "Cannot inspect target API image capability." >&2
    return 1
  }
  if [[ "$capability" != "v1" ]]; then
    echo "Target API image lacks the required migration startup guard." >&2
    return 1
  fi
}

horse_id_migration_assert_api_paused() {
  local api_base_url=${1:?API base URL is required}
  local api_key=${2:?API key is required}
  local python_command=${HRP_PYTHON3:-python3}
  "$python_command" - "$api_base_url" "$api_key" <<'PY'
import json
import sys
from urllib.error import URLError
from urllib.parse import urlparse
from urllib.request import Request, urlopen

base, api_key = sys.argv[1:]
if urlparse(base).scheme != "https" or not api_key:
    print("Cannot confirm the persisted collection pause.", file=sys.stderr)
    raise SystemExit(1)
request = Request(base.rstrip("/") + "/api/v2/admin/collection/pipeline-state",
                  headers={"X-Api-Key": api_key})
try:
    with urlopen(request, timeout=10) as response:
        payload = response.read(65537)
    if len(payload) > 65536:
        raise ValueError("response exceeds the fixed limit")
    state = json.loads(payload)
    if not isinstance(state, dict) or not isinstance(state.get("pipeline"), dict) or state["pipeline"].get("isPaused") is not True:
        raise ValueError("pipeline is not paused")
except (OSError, URLError, ValueError, json.JSONDecodeError):
    print("Cannot confirm the persisted collection pause; refusing API stop.", file=sys.stderr)
    raise SystemExit(1)
PY
}

horse_id_migration_assert_api_container_exited() {
  local container=${1:?API container is required}
  local metadata
  metadata="$(docker inspect --format '{{.State.Status}} {{.State.Running}} {{.State.Restarting}} {{.State.Paused}} {{.State.Pid}}' "$container")" || {
    echo "Could not inspect the former API container after stop." >&2
    return 1
  }
  read -r status running restarting paused pid <<< "$metadata"
  [ "$status" = "exited" ] && [ "$running" = "false" ] && [ "$restarting" = "false" ] && [ "$paused" = "false" ] && [ "$pid" = "0" ] || {
    echo "Former API container did not reach a fully stopped state." >&2
    return 1
  }
  [ -z "$(docker compose --env-file .env ps --status running -q api)" ] || {
    echo "A Compose API container remains running after the bounded stop." >&2
    return 1
  }
}

horse_id_migration_validate_operator_binding() {
  local data_directory=${1:?data directory is required}
  local operation_id=${2:?operation GUID is required}
  local python_command=${HRP_PYTHON3:-python3}
  shift 2
  local -a source_pairs=("$@")
  horse_id_migration_validate_guid "$operation_id" || return 1
  ((${#source_pairs[@]} > 0 && ${#source_pairs[@]} % 2 == 0)) || return 1
  timeout --signal=TERM --kill-after=5s 60s "$python_command" - "$data_directory" "$operation_id" "${source_pairs[@]}" <<'PY'
import hashlib
import os
import re
import sqlite3
import stat
import sys
from pathlib import Path
from uuid import UUID

data = Path(sys.argv[1])
requested = sys.argv[2]
arguments = sys.argv[3:]
hash_re = re.compile(r"[0-9A-F]{64}")

def reject():
    print("Operator intent and maintenance-gate binding is unknown or unsafe.", file=sys.stderr)
    raise SystemExit(1)

def read_stable(path, max_bytes):
    before = path.lstat()
    if not stat.S_ISREG(before.st_mode) or before.st_nlink != 1 or before.st_size > max_bytes or path.is_symlink():
        reject()
    fd = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_CLOEXEC", 0))
    try:
        opened = os.fstat(fd)
        if (opened.st_dev, opened.st_ino, opened.st_size, opened.st_mtime_ns, opened.st_ctime_ns) != (before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns, before.st_ctime_ns):
            reject()
        content = os.read(fd, max_bytes + 1)
        after = os.fstat(fd)
        if len(content) != before.st_size or (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns, after.st_ctime_ns) != (opened.st_dev, opened.st_ino, opened.st_size, opened.st_mtime_ns, opened.st_ctime_ns):
            reject()
        return content
    finally:
        os.close(fd)

try:
    if not data.is_dir() or data.is_symlink() or len(arguments) == 0 or len(arguments) % 2:
        reject()
    if str(UUID(requested)) != requested or UUID(requested).int == 0:
        reject()
    data_root = str(Path(os.path.abspath(data)))
    if os.path.realpath(data_root) != data_root:
        reject()
    pairs = []
    for index in range(0, len(arguments), 2):
        source = Path(arguments[index])
        logical = arguments[index + 1]
        resolved = Path(os.path.abspath(source))
        info = resolved.lstat()
        if (not source.is_absolute() or str(source) != str(resolved)
                or not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or resolved.is_symlink()
                or os.path.realpath(resolved) != str(resolved)):
            reject()
        try:
            if os.path.commonpath((data_root, str(resolved))) != data_root:
                reject()
        except ValueError:
            reject()
        if (not logical.startswith("/") or logical == "/" or logical.endswith("/")
                or any(part in (".", "..") for part in logical.split("/"))
                or any(character.isspace() for character in logical)
                or logical.rsplit("/", 1)[-1] != resolved.name):
            reject()
        pairs.append((str(resolved), logical))
    canonical = [physical for physical, _ in pairs]
    logical_paths = [logical for _, logical in pairs]
    if len(set(canonical)) != len(canonical) or len(set(logical_paths)) != len(logical_paths):
        reject()
    if logical_paths != sorted(logical_paths, key=lambda value: value.encode("utf-16-be")):
        reject()
    source_hash = hashlib.sha256("\n".join(logical_paths).encode("utf-8")).hexdigest().upper()
    database = data / "horse-identity-migration-journal.db"
    marker = data / "horse-identity-migration-initialized.marker"
    lock = data / "horse-identity-migration-operation.lock"
    marker_bytes = read_stable(marker, 64)
    if marker_bytes != b"horse-identity-migration-journal-v1":
        reject()
    read_stable(lock, 64)
    read_stable(database, 64 * 1024 * 1024)
    connection = sqlite3.connect(database.resolve(strict=True).as_uri() + "?mode=ro", uri=True, timeout=5)
    try:
        connection.execute("PRAGMA query_only=ON")
        if connection.execute("PRAGMA query_only").fetchone() != (1,) or connection.execute("PRAGMA quick_check(1)").fetchone() != ("ok",):
            reject()
        tables = {row[0] for row in connection.execute("SELECT name FROM sqlite_master WHERE type='table'")}
        required = {"migration_operations", "migration_execution_intents"}
        if not required.issubset(tables):
            reject()
        operation_columns = {row[1] for row in connection.execute("PRAGMA table_info(migration_operations)")}
        intent_columns = {row[1] for row in connection.execute("PRAGMA table_info(migration_execution_intents)")}
        if not {"operation_id", "request_hash", "request_json", "status"}.issubset(operation_columns) or not {"operation_id", "immutable_request_hash", "phase"}.issubset(intent_columns):
            reject()
        operation_rows = connection.execute("SELECT operation_id, request_hash, request_json, status FROM migration_operations WHERE operation_id=?", (requested,)).fetchall()
        intent_rows = connection.execute("SELECT operation_id, immutable_request_hash, phase FROM migration_execution_intents WHERE operation_id=?", (requested,)).fetchall()
    finally:
        connection.close()
    if len(operation_rows) != 1 or len(intent_rows) != 1 or intent_rows[0][0] != requested:
        reject()
    operation_id, operation_hash, request_json, status = operation_rows[0]
    intent_id, immutable_request_hash, phase = intent_rows[0]
    if (operation_id != requested or intent_id != requested
            or not isinstance(operation_hash, str) or not hash_re.fullmatch(operation_hash)
            or not isinstance(request_json, str) or len(request_json.encode("utf-8")) > 1024 * 1024
            or hashlib.sha256(request_json.encode("utf-8")).hexdigest().upper() != operation_hash
            or not isinstance(immutable_request_hash, str) or not hash_re.fullmatch(immutable_request_hash)
            or not isinstance(status, int) or status not in range(7)
            or not isinstance(phase, int) or phase not in range(11)):
        reject()
    gates = []
    for source in canonical:
        gate = Path(source).parent / ("." + Path(source).name + ".horse-identity-migration.maintenance")
        fields = read_stable(gate, 1024).decode("ascii").split("\n")
        if len(fields) != 7 or fields[0] != "v1" or fields[1] != requested or fields[2] != immutable_request_hash or fields[3] != source_hash or not fields[4].isdigit() or int(fields[4]) <= 0 or not fields[5].isdigit() or int(fields[5]) <= 0 or not hash_re.fullmatch(fields[6]):
            reject()
        gates.append(gate)
    print("PASS code=OPERATOR_BINDING sources=%d gates=%d phase=%d" % (len(canonical), len(gates), phase))
except SystemExit:
    raise
except (OSError, UnicodeError, sqlite3.Error, ValueError, TypeError):
    reject()
PY
}

horse_id_migration_assert_operator_status() {
  local api_base_url=${1:?API base URL is required}
  local api_key=${2:?API key is required}
  local operation_id=${3:?operation GUID is required}
  local python_command=${HRP_PYTHON3:-python3}
  HRP_OPERATOR_API_KEY="$api_key" "$python_command" - "$api_base_url" "$operation_id" <<'PY'
import json
import os
import sys
from urllib.error import HTTPError, URLError
from urllib.parse import urlparse
from urllib.request import HTTPRedirectHandler, Request, build_opener

base, requested = sys.argv[1:]
api_key = os.environ.pop("HRP_OPERATOR_API_KEY", "")
if urlparse(base).scheme != "https" or not api_key:
    raise SystemExit(1)
class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None
opener = build_opener(NoRedirect())
try:
    request = Request(base.rstrip("/") + "/api/v2/admin/horse-id-migration-operations/" + requested,
                      headers={"X-Api-Key": api_key})
    with opener.open(request, timeout=10) as response:
        if response.status != 200:
            raise ValueError()
        payload = response.read(65537)
    if len(payload) > 65536:
        raise ValueError()
    body = json.loads(payload)
    if not isinstance(body, dict) or body.get("operationId") != requested or not isinstance(body.get("status"), str):
        raise ValueError()
except (OSError, URLError, HTTPError, ValueError, TypeError, json.JSONDecodeError):
    print("Authenticated operation status did not match the requested bound operation.", file=sys.stderr)
    raise SystemExit(1)
print("PASS code=OPERATOR_STATUS")
PY
}

horse_id_migration_assert_operator_ui_ready() {
  local api_base_url=${1:?API base URL is required}
  local api_key=${2:?API key is required}
  local operation_id=${3:?operation GUID is required}
  local python_command=${HRP_PYTHON3:-python3}
  HRP_OPERATOR_API_KEY="$api_key" "$python_command" - "$api_base_url" "$operation_id" <<'PY'
import http.cookiejar
import json
import os
import sys
import time
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode, urlparse
from urllib.request import HTTPRedirectHandler, HTTPCookieProcessor, Request, build_opener

base, requested = sys.argv[1:]
api_key = os.environ.pop("HRP_OPERATOR_API_KEY", "")
if urlparse(base).scheme != "https" or not api_key:
    raise SystemExit(1)
jar = http.cookiejar.CookieJar()
base_origin = urlparse(base)
class SameOriginRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        target = urlparse(newurl)
        if target.scheme != base_origin.scheme or target.netloc != base_origin.netloc:
            return None
        return super().redirect_request(req, fp, code, msg, headers, newurl)
opener = build_opener(SameOriginRedirect(), HTTPCookieProcessor(jar))
def request(path, headers=None, data=None, expected=None):
    req = Request(base.rstrip("/") + path, headers=headers or {}, data=data)
    try:
        response = opener.open(req, timeout=10)
    except HTTPError as error:
        if expected is not None and error.code == expected:
            return error.code, error.read(65537)
        raise
    if expected is not None and response.status != expected:
        raise ValueError()
    return response.status, response.read(1024 * 1024 + 1)

def read_only_readiness():
    status, payload = request("/health", expected=200)
    if json.loads(payload).get("status") != "ok":
        raise ValueError()
    status, payload = request("/api/v2/admin/horse-id-migration-operations/" + requested,
                              headers={"X-Api-Key": api_key}, expected=200)
    body = json.loads(payload)
    if body.get("operationId") != requested:
        raise ValueError()
    status, payload = request("/api/v2/admin/collection/pipeline-state",
                              headers={"X-Api-Key": api_key}, expected=503)
    blocked = json.loads(payload)
    if blocked.get("title") != "Application startup is blocked by migration state.":
        raise ValueError()

try:
    deadline = time.monotonic() + 120
    while True:
        try:
            read_only_readiness()
            break
        except (OSError, URLError, HTTPError, ValueError, TypeError, UnicodeError, json.JSONDecodeError):
            if time.monotonic() >= deadline:
                raise
            time.sleep(1)
    login = urlencode({"username": "user", "password": api_key,
                       "ReturnUrl": "/settings/horse-id-migration?operationId=" + requested}).encode()
    request("/login", headers={"Content-Type": "application/x-www-form-urlencoded",
                                "Accept": "text/html"}, data=login)
    status, html = request("/settings/horse-id-migration?operationId=" + requested,
                           headers={"Accept": "text/html"}, expected=200)
    text = html.decode("utf-8")
    if requested not in text or "Status:" not in text:
        raise ValueError()
except (OSError, URLError, HTTPError, ValueError, TypeError, UnicodeError, json.JSONDecodeError):
    print("Operator-only protected host did not pass liveness, status, HTML, and business-block checks.", file=sys.stderr)
    raise SystemExit(1)
print("PASS code=OPERATOR_UI_READY")
PY
}

horse_id_migration_assert_api_startup_blocked() {
  local api_base_url=${1:?API base URL is required}
  local api_key=${2:?API key is required}
  local python_command=${HRP_PYTHON3:-python3}
  HRP_RECOVERY_API_KEY="$api_key" "$python_command" - "$api_base_url" <<'PY'
import json
import os
import sys
from urllib.error import HTTPError, URLError
from urllib.parse import urlparse
from urllib.request import HTTPRedirectHandler, Request, build_opener

base = sys.argv[1]
api_key = os.environ.pop("HRP_RECOVERY_API_KEY", "")
if urlparse(base).scheme != "https" or not api_key:
    raise SystemExit(1)
class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None
opener = build_opener(NoRedirect())
request = Request(base.rstrip("/") + "/api/v2/admin/collection/operations/runtime-status",
                  headers={"X-Api-Key": api_key})
status, payload = 0, b""
try:
    with opener.open(request, timeout=10) as response:
        status, payload = response.status, response.read(65537)
except HTTPError as response:
    status, payload = response.code, response.read(65537)
except (OSError, URLError):
    status, payload = 0, b""
try:
    value = json.loads(payload) if len(payload) <= 65536 else None
except (UnicodeError, json.JSONDecodeError):
    value = None
if (status != 503 or not isinstance(value, dict) or value.get("status") != 503
        or value.get("title") != "Application startup is blocked by migration state."):
    print("Expected authenticated migration-blocked API response was not observed.", file=sys.stderr)
    raise SystemExit(1)
PY
}

# Inspect persisted collection state without constructing CollectionPlatformStore
# or running its schema migrator. The helper deliberately understands only the
# deployed collection schema version and known state values.
horse_id_migration_assert_recovery_collection_state() {
  local database=${1:?collection database path is required}
  local minimum_age_seconds=${2:-0}
  local python_command=${HRP_PYTHON3:-python3}
  timeout --signal=TERM --kill-after=5s 60s "$python_command" - "$database" "$minimum_age_seconds" <<'PY'
import datetime
import re
import sqlite3
import stat
import sys
from pathlib import Path

path = Path(sys.argv[1])
minimum_age = int(sys.argv[2])
def fail():
    print("Persisted collection pause or work state is unknown or unsafe; refusing API startup recovery.", file=sys.stderr)
    raise SystemExit(1)

paused_timestamp = None
try:
    info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or path.is_symlink():
        fail()
    resolved = path.resolve(strict=True)
    connection = sqlite3.connect(resolved.as_uri() + "?mode=ro", uri=True, timeout=5)
    try:
        connection.execute("PRAGMA query_only=ON")
        if connection.execute("PRAGMA query_only").fetchone() != (1,):
            fail()
        if connection.execute("PRAGMA integrity_check").fetchall() != [("ok",)]:
            fail()
        tables = {row[0] for row in connection.execute(
            "SELECT name FROM sqlite_master WHERE type='table'")}
        required = {"collection_schema_history", "collection_platform_controls",
                    "collection_tasks", "collection_execution_leases"}
        if not required.issubset(tables):
            fail()
        schema_version = connection.execute("SELECT MAX(version) FROM collection_schema_history").fetchone()
        if schema_version != (25,):
            fail()
        expected_columns = {
            "collection_platform_controls": {"ControlId", "IsPaused"},
            "collection_tasks": {"Status"},
            "collection_execution_leases": {"Status"},
        }
        for table, columns in expected_columns.items():
            actual = {row[1] for row in connection.execute(f"PRAGMA table_info({table})")}
            if not columns.issubset(actual):
                fail()
        control_columns = {row[1] for row in connection.execute("PRAGMA table_info(collection_platform_controls)")}
        pipeline_select = "IsPaused, UpdatedAt, typeof(IsPaused)" if "UpdatedAt" in control_columns else "IsPaused, NULL, typeof(IsPaused)"
        pipeline = connection.execute(
            f"SELECT {pipeline_select} FROM collection_platform_controls WHERE ControlId='pipeline'").fetchall()
        if len(pipeline) != 1 or pipeline[0][0] != 1 or pipeline[0][2] != "integer":
            fail()
        if minimum_age < 0:
            fail()
        if minimum_age:
            if "UpdatedAt" not in control_columns:
                fail()
            if not isinstance(pipeline[0][1], str):
                fail()
            stored = pipeline[0][1]
            timestamp_match = re.fullmatch(
                r"(?P<date>\d{4}-\d{2}-\d{2})[ T](?P<time>\d{2}:\d{2}:\d{2})(?:\.(?P<fraction>\d{1,7}))?",
                stored)
            if timestamp_match is None:
                fail()
            # CollectionPlatformStore persists JstTime.ToDatabase through the
            # EF SQLite DateTime mapping (space separator, up to seven
            # fractional digits); migration JSON uses the equivalent T form.
            # Parse both actual producer forms as timezone-less JST, then
            # reject future timestamps before applying the drain age.
            fraction = (timestamp_match.group("fraction") or "").ljust(6, "0")[:6]
            paused_at = datetime.datetime.strptime(
                f"{timestamp_match.group('date')} {timestamp_match.group('time')}.{fraction}",
                "%Y-%m-%d %H:%M:%S.%f").replace(
                    tzinfo=datetime.timezone(datetime.timedelta(hours=9)))
            age = (datetime.datetime.now(datetime.timezone(datetime.timedelta(hours=9))) - paused_at).total_seconds()
            if age < minimum_age:
                fail()
            paused_timestamp = stored
        task_states = {row[0] for row in connection.execute("SELECT DISTINCT Status FROM collection_tasks")}
        known_tasks = {"Pending", "Ready", "Running", "RetryWaiting", "WaitingDiscovery",
                       "Succeeded", "Failed", "Cancelled", "DeadLetter"}
        if not task_states.issubset(known_tasks):
            fail()
        if connection.execute("SELECT 1 FROM collection_tasks WHERE Status='Running' LIMIT 1").fetchone() is not None:
            fail()
        lease_states = {row[0] for row in connection.execute("SELECT DISTINCT Status FROM collection_execution_leases")}
        if not lease_states.issubset({"StartPending", "Running", "Completed", "Expired"}):
            fail()
        if connection.execute(
            "SELECT 1 FROM collection_execution_leases WHERE Status IN ('StartPending','Running') LIMIT 1").fetchone() is not None:
            fail()
    finally:
        connection.close()
except SystemExit:
    raise
except (OSError, sqlite3.Error, ValueError, TypeError, RuntimeError):
    fail()
print("PASS code=RECOVERY_QUIESCENT" + (" updated_at=" + paused_timestamp if paused_timestamp is not None else ""))
PY
}

horse_id_migration_assert_recovery_api_ready() {
  local api_base_url=${1:?API base URL is required}
  local api_key=${2:?API key is required}
  local python_command=${HRP_PYTHON3:-python3}
  HRP_RECOVERY_API_KEY="$api_key" "$python_command" - "$api_base_url" <<'PY'
import json
import os
import sys
from urllib.error import HTTPError, URLError
from urllib.parse import urlparse
from urllib.request import HTTPRedirectHandler, Request, build_opener

base = sys.argv[1].rstrip("/")
api_key = os.environ.pop("HRP_RECOVERY_API_KEY", "")
if urlparse(base).scheme != "https" or not api_key:
    raise SystemExit(1)
class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None
opener = build_opener(NoRedirect())
def get(path, authenticated=False):
    headers = {"X-Api-Key": api_key} if authenticated else {}
    request = Request(base + path, headers=headers)
    with opener.open(request, timeout=10) as response:
        if response.status != 200:
            raise ValueError()
        payload = response.read(1024 * 1024 + 1)
    if len(payload) > 1024 * 1024:
        raise ValueError()
    return json.loads(payload)

try:
    pipeline = get("/api/v2/admin/collection/pipeline-state", True)
    tasks = get("/api/v2/admin/collection/tasks?statuses=Running&pageSize=1", True)
    if (not isinstance(pipeline, dict) or not isinstance(pipeline.get("pipeline"), dict)
        or pipeline["pipeline"].get("isPaused") is not True
        or not isinstance(tasks, dict) or not isinstance(tasks.get("page"), dict)
        or not isinstance(tasks["page"].get("items"), list) or tasks["page"]["items"]):
        raise ValueError()
except (OSError, URLError, HTTPError, ValueError, TypeError, json.JSONDecodeError):
    print("API startup recovery readiness or paused-state readback failed.", file=sys.stderr)
    raise SystemExit(1)
print("PASS code=RECOVERY_API_READY_PAUSED")
PY
}

# Wait only for transport/application startup. Once Swagger responds, the
# authenticated paused-state readback is performed once and fails immediately
# on an unexpected state rather than retrying a potentially unsafe API.
horse_id_migration_wait_recovery_swagger() {
  local api_base_url=${1:?API base URL is required}
  local python_command=${HRP_PYTHON3:-python3}
  timeout --signal=TERM --kill-after=5s 280s "$python_command" - "$api_base_url" <<'PY'
import json
import sys
import time
from urllib.error import HTTPError, URLError
from urllib.request import urlopen

url = sys.argv[1].rstrip("/") + "/swagger/v1/swagger.json"
for attempt in range(18):
    try:
        with urlopen(url, timeout=5) as response:
            if response.status == 200:
                payload = response.read(1024 * 1024 + 1)
                if len(payload) > 1024 * 1024 or not isinstance(json.loads(payload), dict):
                    raise SystemExit("Swagger response was malformed.")
                print("PASS code=RECOVERY_SWAGGER_READY")
                raise SystemExit(0)
    except HTTPError as response:
        if response.code not in (502, 503, 504):
            raise SystemExit("API returned a non-retryable startup response.")
    except (OSError, URLError, TimeoutError):
        pass
    if attempt < 17:
        time.sleep(10)
raise SystemExit("API Swagger did not become ready within the bounded startup window.")
PY
}

# Change only the two validated image coordinates in the remote Compose .env.
# All unrelated bytes are preserved, the replacement is atomic and private,
# and a concurrent configuration replacement fails closed.
horse_id_migration_update_recovery_env() {
  local env_file=${1:?Compose environment file is required}
  local image_name=${2:?target image name is required}
  local image_tag=${3:?target image tag is required}
  local python_command=${HRP_PYTHON3:-python3}
  timeout --signal=TERM --kill-after=5s 30s "$python_command" - "$env_file" "$image_name" "$image_tag" <<'PY'
import os
import re
import stat
import sys
import tempfile
from pathlib import Path

path = Path(sys.argv[1])
image_name, image_tag = sys.argv[2:]
if not re.fullmatch(r"ghcr\.io/[a-z0-9._-]+/horse-racing-prediction-api", image_name):
    raise SystemExit("Recovery image name is invalid.")
if not re.fullmatch(r"sha-[0-9a-f]{40}", image_tag):
    raise SystemExit("Recovery image tag is invalid.")
try:
    before = path.lstat()
    if not stat.S_ISREG(before.st_mode) or before.st_nlink != 1 or path.is_symlink() or before.st_size > 1024 * 1024:
        raise ValueError()
    read_fd = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
    try:
        opened = os.fstat(read_fd)
        if (not stat.S_ISREG(opened.st_mode) or opened.st_nlink != 1 or
                (opened.st_dev, opened.st_ino) != (before.st_dev, before.st_ino) or
                opened.st_size != before.st_size or opened.st_size > 1024 * 1024):
            raise ValueError()
        original = os.read(read_fd, 1024 * 1024 + 1)
        after_read = os.fstat(read_fd)
        if (len(original) != before.st_size or b"\x00" in original or
                (after_read.st_dev, after_read.st_ino, after_read.st_size) !=
                (opened.st_dev, opened.st_ino, opened.st_size)):
            raise ValueError()
    finally:
        os.close(read_fd)
    text = original.decode("utf-8", errors="strict")
    lines = text.splitlines(keepends=True)
    values = {"IMAGE_NAME": image_name, "IMAGE_TAG": image_tag}
    found = set()
    output = []
    for line in lines:
        content = line.rstrip("\r\n")
        match = re.match(r"^(?:export\s+)?(IMAGE_NAME|IMAGE_TAG)\s*=", content)
        if match:
            key = match.group(1)
            if key in found:
                raise ValueError()
            found.add(key)
            ending = line[len(content):]
            output.append(f"{key}={values[key]}{ending}")
        else:
            output.append(line)
    ending = "\r\n" if b"\r\n" in original else "\n"
    for key in ("IMAGE_NAME", "IMAGE_TAG"):
        if key not in found:
            if output and not output[-1].endswith(("\n", "\r")):
                output[-1] += ending
            output.append(f"{key}={values[key]}{ending}")
    replacement = "".join(output).encode("utf-8")
    current = path.lstat()
    if (before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns) != (
            current.st_dev, current.st_ino, current.st_size, current.st_mtime_ns):
        raise ValueError()
    fd, temp_name = tempfile.mkstemp(prefix=".env.recovery.", dir=str(path.parent))
    try:
        os.fchmod(fd, 0o600)
        with os.fdopen(fd, "wb", closefd=True) as stream:
            stream.write(replacement)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temp_name, path)
        directory_fd = os.open(path.parent, os.O_RDONLY | getattr(os, "O_DIRECTORY", 0))
        try:
            os.fsync(directory_fd)
        finally:
            os.close(directory_fd)
    except Exception:
        try:
            os.unlink(temp_name)
        except OSError:
            pass
        raise
except (OSError, UnicodeError, ValueError):
    raise SystemExit("Compose image configuration could not be safely updated.")
PY
}

# Resolve every physical SQLite file from the rendered compose service, not a
# second copy of deployment defaults. The appsettings default queue provider is
# Sqs when the rendered environment omits an override.
horse_id_migration_configured_store_paths() {
  local data_directory=${1:?data directory is required}
  local python_command=${HRP_PYTHON3:-python3}
  local python_code status had_pipefail=no
  read -r -d '' python_code <<'PY' || true
import json
import re
import sys
from pathlib import Path, PurePosixPath

def reject(where="configuration"):
    print("Cannot resolve configured SQLite stores (" + where + "); refusing migration.", file=sys.stderr)
    raise SystemExit(1)

try:
    data = Path(sys.argv[1])
    if data.is_symlink() or not data.is_dir():
        reject("data root")
    rendered_config = sys.stdin.buffer.read(1024 * 1024 + 1)
    if len(rendered_config) > 1024 * 1024:
        reject("rendered compose size")
    config = json.loads(rendered_config)
    api = config["services"]["api"]
    environment = api.get("environment") or {}
    if not isinstance(environment, dict):
        reject("API environment")
    mounts = [volume for volume in api.get("volumes", [])
              if volume.get("target") == "/data" and volume.get("type") == "bind"]
    if len(mounts) != 1 or Path(mounts[0]["source"]).resolve(strict=True) != data.resolve(strict=True):
        reject("persistent /data bind")

    def relative_database(path):
        candidate = PurePosixPath(path)
        if not candidate.is_absolute() or candidate.suffix.lower() != ".db" or ".." in candidate.parts:
            reject()
        try:
            relative = candidate.relative_to("/data").as_posix()
        except ValueError:
            reject()
        if relative in (".", "") or re.search(r"\s|\\", relative):
            reject()
        return relative

    connection = environment.get("ConnectionStrings__EventStore", "")
    match = re.search(r"(?:^|;)\s*Data Source\s*=\s*([^;]+)", connection, re.IGNORECASE)
    if match is None:
        reject("event database setting")
    paths = [relative_database(match.group(1).strip())]
    for directory_key, file_key in (
        ("CollectionPlatform__StateDirectory", "CollectionPlatform__DatabaseFileName"),
        ("PredictionScheduling__StateDirectory", "PredictionScheduling__StoreFileName"),
    ):
        directory = environment.get(directory_key)
        filename = environment.get(file_key)
        if not isinstance(directory, str) or not isinstance(filename, str) or not filename or "/" in filename or "\\" in filename:
            reject("configured database setting")
        paths.append(relative_database(str(PurePosixPath(directory) / filename)))

    provider = str(environment.get("CollectionQueue__Provider", "Sqs")).strip().lower()
    if provider == "local":
        queue_path = str(environment.get("CollectionQueue__LocalDatabasePath", "collection-platform-state/local-collection-queue.db"))
        # A relative Local queue resolves under the image's /app working directory,
        # which is not in the persistent bind. Only an explicit /data path is safe.
        paths.append(relative_database(queue_path))
    elif provider != "sqs":
        reject()

    paths.append("horse-identity-migration-journal.db")
    if len(paths) != len(set(paths)):
        reject("duplicate database paths")
    for relative in paths:
        source = data / relative
        info = source.lstat()
        try:
            source.resolve(strict=True).relative_to(data.resolve(strict=True))
        except ValueError:
            reject()
        if not source.is_file() or source.is_symlink() or info.st_size < 0:
            reject("missing or unsafe configured database")
    print("\n".join(paths))
except SystemExit:
    raise
except (OSError, KeyError, TypeError, ValueError, json.JSONDecodeError):
    reject("rendered compose data")
PY
  [[ ":$SHELLOPTS:" == *":pipefail:"* ]] && had_pipefail=yes
  set -o pipefail
  if docker compose --env-file .env config --format json 2>/dev/null |
      "$python_command" -c "$python_code" "$data_directory"; then
    status=0
  else
    status=$?
  fi
  if [[ "$had_pipefail" == no ]]; then set +o pipefail; fi
  return "$status"
}

# Run a read-only integrity check over the configured application stores after
# the API has restarted. The migration journal has its own preflight and is not
# an application store. Do not require quiescent sidecars here: the live API
# may legitimately have an open WAL after restart.
horse_id_migration_check_configured_store_integrity() {
  local data_directory=${1:?data directory is required}
  local python_command=${HRP_PYTHON3:-python3}
  local store_set store status checked expected
  local -a configured_stores=() application_stores=()

  if ! store_set=$(horse_id_migration_configured_store_paths "$data_directory" 2>/dev/null); then
    echo "FAIL code=STORE_SET checked=0" >&2
    return 1
  fi
  mapfile -t configured_stores <<< "$store_set"
  for store in "${configured_stores[@]}"; do
    [[ "$store" == "horse-identity-migration-journal.db" ]] && continue
    application_stores+=("$store")
  done
  expected=${#application_stores[@]}
  if (( expected < 3 )); then
    echo "FAIL code=STORE_SET checked=0" >&2
    return 1
  fi

  local output
  if output=$(timeout --signal=TERM --kill-after=5s 90s \
      "$python_command" - "$data_directory" "${application_stores[@]}" 2>/dev/null <<'PY'
import sqlite3
import sys
from pathlib import Path

root = Path(sys.argv[1])
stores = sys.argv[2:]
checked = 0

def fail(code):
    raise SystemExit(code)

try:
    if root.is_symlink() or not root.is_dir() or len(stores) < 3:
        fail(10)
    root = root.resolve(strict=True)
    for relative in stores:
        candidate = Path(relative)
        if candidate.is_absolute() or ".." in candidate.parts or candidate.suffix.lower() != ".db":
            fail(10)
        database = root / candidate
        if database.is_symlink() or not database.is_file():
            fail(10)
        database.resolve(strict=True).relative_to(root)
        connection = sqlite3.connect(database.resolve(strict=True).as_uri() + "?mode=ro",
                                     uri=True, timeout=5)
        try:
            connection.execute("PRAGMA query_only=ON")
            rows = connection.execute("PRAGMA integrity_check")
            first = rows.fetchone()
            second = rows.fetchone()
            checked += 1
            if first != ("ok",) or second is not None:
                fail(11)
        except sqlite3.Error:
            fail(12)
        finally:
            connection.close()
        print("ok", flush=True)
except SystemExit:
    raise
except (OSError, sqlite3.Error, ValueError, RuntimeError):
    fail(12)
PY
  ); then
    checked=$(printf '%s\n' "$output" | awk '$0 == "ok" { count++ } END { print count+0 }')
    if [[ "$checked" != "$expected" ]]; then
      echo "FAIL code=STORE_COUNT checked=$checked" >&2
      return 1
    fi
    printf '%s\n' "$output"
    echo "PASS code=INTEGRITY_CHECK checked=$checked"
    return 0
  else
    status=$?
    checked=$(printf '%s\n' "$output" | awk '$0 == "ok" { count++ } END { print count+0 }')
    case "$status" in
      10) echo "FAIL code=STORE_OPEN checked=$checked" >&2 ;;
      11) echo "FAIL code=INTEGRITY_RESULT checked=$checked" >&2 ;;
      12) echo "FAIL code=INTEGRITY_QUERY checked=$checked" >&2 ;;
      124|137|143) echo "FAIL code=INTEGRITY_DEADLINE checked=$checked" >&2 ;;
      *) echo "FAIL code=INTEGRITY_QUERY checked=$checked" >&2 ;;
    esac
    return 1
  fi
}

# Returns one verified Succeeded GUID, if present. SQL here is a bounded,
# read-only blocker; the API's fixed GUID command remains the canonical receipt
# validator and is always run before a compose-up over an initialized journal.
horse_id_migration_journal_preflight() {
  local data_directory=${1:?data directory is required}
  local requested_operation_id=${2-}
  local python_command=${HRP_PYTHON3:-python3}
  "$python_command" - "$data_directory" "$requested_operation_id" <<'PY'
import os
import sqlite3
import stat
import sys
from pathlib import Path
from urllib.parse import quote
from uuid import UUID

data = Path(sys.argv[1])
requested = sys.argv[2].lower()
marker = data / "horse-identity-migration-initialized.marker"
database = data / "horse-identity-migration-journal.db"
lock = data / "horse-identity-migration-operation.lock"
companions = [database] + [Path(str(database) + suffix) for suffix in ("-journal", "-wal", "-shm")]

def reject():
    print("Migration journal preflight failed closed.", file=sys.stderr)
    raise SystemExit(1)

def regular_bounded(path, max_bytes):
    try:
        info = path.lstat()
    except FileNotFoundError:
        return None
    if not stat.S_ISREG(info.st_mode) or info.st_size > max_bytes:
        reject()
    return info.st_size

try:
    if data.is_symlink():
        reject()
    marker_size = regular_bounded(marker, 64)
    database_size = regular_bounded(database, 64 * 1024 * 1024)
    if marker_size is None and database_size is None:
        if requested:
            reject()
        print("")
        raise SystemExit(0)
    if marker_size is None or database_size is None:
        reject()
    with marker.open("rb") as marker_stream:
        marker_content = marker_stream.read(65)
    if len(marker_content) > 64 or marker_content != b"horse-identity-migration-journal-v1":
        reject()
    total = marker_size + database_size
    for path in companions[1:]:
        size = regular_bounded(path, 64 * 1024 * 1024)
        if size is not None:
            total += size
    lock_size = regular_bounded(lock, 64)
    if lock_size is None:
        reject()
    total += lock_size
    if total > 64 * 1024 * 1024:
        reject()

    uri = "file:" + quote(str(database), safe="/:") + "?mode=ro"
    connection = sqlite3.connect(uri, uri=True, timeout=3.0)
    try:
        connection.execute("PRAGMA query_only = ON")
        connection.execute("PRAGMA busy_timeout = 3000")
        if connection.execute("PRAGMA quick_check(1)").fetchone() != ("ok",):
            reject()
        required_tables = {"migration_operations", "migration_pages", "migration_required_pages"}
        found_tables = {row[0] for row in connection.execute(
            "SELECT name FROM sqlite_master WHERE type='table' AND name IN "
            "('migration_operations','migration_pages','migration_required_pages')")}
        if found_tables != required_tables:
            reject()
        columns = {row[1] for row in connection.execute("PRAGMA table_info(migration_operations)")}
        if not {"operation_id", "status", "reason", "terminal_utc", "workflow_claimed"}.issubset(columns):
            reject()
        rows = connection.execute(
            "SELECT operation_id,status,reason,terminal_utc,workflow_claimed "
            "FROM migration_operations ORDER BY operation_id LIMIT 4097").fetchall()
        if len(rows) > 4096:
            reject()
    finally:
        connection.close()

    states = {}
    normalized_ids = set()
    successes = []
    for operation_id, _status, _reason, _terminal, _claimed in rows:
        if not isinstance(operation_id, str):
            reject()
        normalized_id = operation_id.lower()
        if normalized_id in normalized_ids:
            reject()
        normalized_ids.add(normalized_id)
    for operation_id, status, reason, terminal, claimed in rows:
        try:
            if str(UUID(operation_id)) != operation_id or len(operation_id) != 36:
                reject()
        except (ValueError, TypeError, AttributeError):
            reject()
        if not isinstance(status, int) or not isinstance(reason, int) or not isinstance(claimed, int) or claimed not in (0, 1):
            reject()
        if status not in range(7) or reason not in range(7):
            reject()
        if status in (0, 1, 2) and reason != 0:
            reject()
        if status == 0 and claimed != 0:
            reject()
        if status in (1, 2, 5, 6) and claimed != 1:
            reject()
        if status in (5, 6) and reason != 6:
            reject()
        if status in (0, 1, 5, 6) and terminal is not None:
            reject()
        if status in (2, 3, 4) and terminal is None:
            reject()
        states[operation_id] = status
        if status == 2:
            successes.append(operation_id)

    if sum(status == 1 for status in states.values()) > 1:
        reject()
    if any(status in (1, 5, 6) for status in states.values()):
        reject()
    staged = [operation_id for operation_id, status in states.items() if status == 0]
    if requested:
        if requested not in states or states[requested] != 0 or staged != [requested]:
            reject()
    elif staged:
        reject()

    print(successes[0] if successes else "")
except SystemExit:
    raise
except (OSError, sqlite3.Error, ValueError, TypeError):
    reject()
PY
}

# Read-only incident evidence for a stopped API. This intentionally does not
# call InspectStartupGuard: that path initializes/upgrades the journal and may
# turn a Running operation into Interrupted.
horse_id_migration_diagnose_startup_journal() {
  local data_directory=${1:?data directory is required}
  local python_command=${HRP_PYTHON3:-python3}
  timeout --signal=TERM --kill-after=2s 30s "$python_command" - "$data_directory" <<'PY'
import os
import re
import sqlite3
import stat
import sys
from pathlib import Path
from urllib.parse import quote

data = Path(sys.argv[1])
marker = data / "horse-identity-migration-initialized.marker"
database = data / "horse-identity-migration-journal.db"
lock = data / "horse-identity-migration-operation.lock"
sidecars = [Path(str(database) + suffix) for suffix in ("-journal", "-wal", "-shm")]

def report(code, **fields):
    safe = " ".join(f"{key}={value}" for key, value in fields.items())
    print(f"JOURNAL code={code}" + (f" {safe}" if safe else ""))

def fail(code):
    report(code)
    raise SystemExit(0)

def unknown(code):
    report(code)
    raise SystemExit(1)

def regular(path, max_bytes):
    try:
        info = path.lstat()
    except FileNotFoundError:
        return None
    if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or info.st_size > max_bytes:
        fail("UNSAFE_FILE_SHAPE")
    return info.st_size

def read_regular(path, max_bytes):
    before = path.lstat()
    if not stat.S_ISREG(before.st_mode) or before.st_nlink != 1 or before.st_size > max_bytes:
        fail("UNSAFE_FILE_SHAPE")
    fd = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_CLOEXEC", 0))
    try:
        opened = os.fstat(fd)
        if (not stat.S_ISREG(opened.st_mode) or opened.st_nlink != 1 or
                (opened.st_dev, opened.st_ino, opened.st_size, opened.st_mtime_ns, opened.st_ctime_ns) !=
                (before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns, before.st_ctime_ns)):
            fail("UNSAFE_FILE_RACE")
        with os.fdopen(fd, "rb", closefd=False) as stream:
            content = stream.read(max_bytes + 1)
        after = os.fstat(fd)
        if len(content) != before.st_size or len(content) > max_bytes or (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns, after.st_ctime_ns) != (opened.st_dev, opened.st_ino, opened.st_size, opened.st_mtime_ns, opened.st_ctime_ns):
            fail("UNSAFE_FILE_RACE")
        return content
    finally:
        os.close(fd)

try:
    if data.is_symlink() or not data.is_dir():
        fail("DATA_DIRECTORY_SHAPE")
    marker_size = regular(marker, 64)
    database_size = regular(database, 64 * 1024 * 1024)
    if marker_size is None and database_size is None:
        report("ABSENT", marker=0, database=0)
        raise SystemExit(0)
    if marker_size is None or database_size is None:
        fail("INITIALIZATION_INCOMPLETE")
    marker_value = read_regular(marker, 64)
    if marker_value != b"horse-identity-migration-journal-v1":
        fail("MARKER_INVALID")
    lock_size = regular(lock, 64)
    if lock_size is None or lock_size != 0:
        fail("OPERATION_LOCK_SHAPE")
    sidecar_flags = []
    for path in sidecars:
        size = regular(path, 64 * 1024 * 1024)
        sidecar_flags.append(0 if size is None else (1 if size == 0 else 2))
    if any(sidecar_flags):
        report("SQLITE_SIDECAR_PRESENT", rollback=sidecar_flags[0], wal=sidecar_flags[1], shm=sidecar_flags[2])
        raise SystemExit(0)

    database_fd = os.open(database, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_CLOEXEC", 0))
    database_info = os.fstat(database_fd)
    if not stat.S_ISREG(database_info.st_mode) or database_info.st_nlink != 1 or database_info.st_size != database_size:
        os.close(database_fd)
        fail("UNSAFE_DATABASE_RACE")
    database_location = f"/proc/self/fd/{database_fd}" if sys.platform.startswith("linux") else str(database)
    uri = "file:" + quote(database_location, safe="/:\\") + "?mode=ro"
    connection = sqlite3.connect(uri, uri=True, timeout=2.0)
    try:
        connection.execute("PRAGMA query_only = ON")
        connection.execute("PRAGMA busy_timeout = 2000")
        integrity = connection.execute("PRAGMA quick_check(1)").fetchone()
        if integrity != ("ok",):
            fail("INTEGRITY_CHECK")
        tables = {row[0] for row in connection.execute("SELECT name FROM sqlite_master WHERE type='table'")}
        required = {"migration_operations", "migration_pages", "migration_required_pages"}
        if not required.issubset(tables):
            unknown("SCHEMA_UNKNOWN")

        def columns(table):
            return {row[1] for row in connection.execute(f'PRAGMA table_info("{table}")')}

        expected = {
            "migration_operations": {"operation_id", "request_hash", "request_json", "status", "reason", "created_utc", "updated_utc", "terminal_utc", "results_json", "workflow_claimed"},
            "migration_pages": {"operation_id", "page_identity", "surface", "table_name", "row_count", "membership_hash", "before_hash", "expected_after_hash", "state", "updated_utc"},
            "migration_required_pages": {"operation_id", "page_identity", "surface", "table_name", "row_count", "membership_hash", "before_hash", "expected_after_hash", "evidence_bytes"},
        }
        actual = {name: columns(name) for name in required}
        if any(not expected[name].issubset(actual[name]) for name in required):
            unknown("SCHEMA_UNKNOWN")
        if len(connection.execute("SELECT 1 FROM migration_operations LIMIT 4097").fetchall()) > 4096:
            fail("ROW_LIMIT")

        state_rows = connection.execute(
            "SELECT status, reason, terminal_utc, workflow_claimed FROM migration_operations"
        ).fetchall()
        counts = [0] * 7
        for status, reason, terminal, claimed in state_rows:
            if not isinstance(status, int) or status not in range(7) or not isinstance(reason, int) or reason not in range(7):
                fail("OPERATION_ENUM")
            if not isinstance(claimed, int) or claimed not in (0, 1):
                fail("WORKFLOW_CLAIM")
            if status in (0, 1, 2) and reason != 0 or status in (5, 6) and reason != 6:
                fail("OPERATION_REASON")
            if status in (0, 1, 5, 6) and terminal is not None or status in (2, 3, 4) and terminal is None:
                fail("OPERATION_TERMINAL")
            counts[status] += 1
        if counts[1] > 1:
            fail("MULTIPLE_RUNNING")

        page_count = connection.execute("SELECT COUNT(*) FROM migration_pages").fetchone()[0]
        required_count = connection.execute("SELECT COUNT(*) FROM migration_required_pages").fetchone()[0]
        if page_count > 1000000 or required_count > 1000000:
            fail("PAGE_LIMIT")
        incomplete = connection.execute("""
            SELECT 1 FROM migration_operations o WHERE o.status=2 AND (
              NOT EXISTS (SELECT 1 FROM migration_required_pages r WHERE r.operation_id=o.operation_id)
              OR (SELECT COUNT(*) FROM migration_pages p WHERE p.operation_id=o.operation_id)
                 <> (SELECT COUNT(*) FROM migration_required_pages r WHERE r.operation_id=o.operation_id)
              OR EXISTS (SELECT 1 FROM migration_required_pages r
                LEFT JOIN migration_pages p ON p.operation_id=r.operation_id AND p.page_identity=r.page_identity
                WHERE r.operation_id=o.operation_id AND (p.page_identity IS NULL OR p.surface<>r.surface
                  OR p.table_name<>r.table_name OR p.row_count<>r.row_count
                  OR p.membership_hash<>r.membership_hash OR p.before_hash<>r.before_hash
                  OR p.expected_after_hash<>r.expected_after_hash OR p.state<>2))
            ) LIMIT 1
        """).fetchone()
        if incomplete:
            fail("SUCCEEDED_EVIDENCE")
        request_kind = "legacy"
        request_table = "migration_request_keys" in tables
        request_columns = columns("migration_request_keys") if request_table else set()
        required_request_columns = {"request_key", "immutable_request_hash", "operation_id"}
        if not request_table or not required_request_columns.issubset(request_columns):
            report("REQUEST_SCHEMA_UNKNOWN", key_table=int(request_table),
                   request_key=int("request_key" in request_columns),
                   immutable_request_hash=int("immutable_request_hash" in request_columns),
                   operation_id=int("operation_id" in request_columns),
                   preview_fingerprint=int("preview_fingerprint" in request_columns),
                   request_kind=int("request_kind" in request_columns), rowcount="unknown",
                   staged=counts[0], running=counts[1], succeeded=counts[2], rejected=counts[3],
                   failed=counts[4], interrupted=counts[5], partial=counts[6])
            raise SystemExit(1)
        request_rows = connection.execute(
            "SELECT COUNT(*) FROM (SELECT 1 FROM migration_request_keys LIMIT 4097)"
        ).fetchone()[0]
        if request_rows > 4096:
            fail("REQUEST_KEY_ROW_LIMIT")
        if request_table:
            if "request_kind" in request_columns:
                request_kind = "current"
                invalid_kind = connection.execute("SELECT 1 FROM migration_request_keys WHERE request_kind NOT IN ('BoundV1','PendingV2') LIMIT 1").fetchone()
                if invalid_kind:
                    fail("REQUEST_KIND")
        staged = connection.execute("SELECT COUNT(*) FROM migration_operations WHERE status=0").fetchone()[0]
        blocked = counts[5] + counts[6]
        code = "READY_SHAPE" if blocked == 0 and counts[1] == 0 else ("INTERRUPTED" if counts[5] else ("PARTIAL_UNVERIFIED" if counts[6] else "RUNNING"))
        report(code, marker=1, integrity="ok", schema="current" if request_kind == "current" else "legacy-upgradable",
               request_key_rows=request_rows,
               staged=staged, running=counts[1], succeeded=counts[2], rejected=counts[3], failed=counts[4],
               interrupted=counts[5], partial=counts[6], pages=page_count, required_pages=required_count)
        database_after = os.fstat(database_fd)
        if (database_after.st_dev, database_after.st_ino, database_after.st_size, database_after.st_mtime_ns, database_after.st_ctime_ns) != (database_info.st_dev, database_info.st_ino, database_info.st_size, database_info.st_mtime_ns, database_info.st_ctime_ns):
            report("UNSAFE_DATABASE_RACE")
            raise SystemExit(1)
    finally:
        connection.close()
        os.close(database_fd)
except SystemExit:
    raise
except Exception:
    report("READ_ONLY_VALIDATION_FAILED")
    raise SystemExit(1)
PY
}

horse_id_migration_verify_recovery_backup() {
  local data_directory=${1:?data directory is required}
  local expected_sha=${2:?expected SHA is required}
  local python_command=${HRP_PYTHON3:-python3}
  shift 2
  local -a expected_members=("$@")
  ((${#expected_members[@]} > 0)) || { echo "BACKUP code=EXPECTED_MEMBERS_MISSING"; return 1; }
  local member
  for member in "${expected_members[@]}"; do
    [[ "$member" =~ ^[A-Za-z0-9._/-]{1,256}$ && "$member" != /* && "$member" != *..* ]] || {
      echo "BACKUP code=EXPECTED_MEMBER_INVALID"
      return 1
    }
  done
  timeout --signal=TERM --kill-after=2s 180s "$python_command" - "$data_directory" "$expected_sha" "${expected_members[@]}" <<'PY'
import hashlib
import os
import re
import stat
import sys
from pathlib import Path

data = Path(sys.argv[1])
expected = sys.argv[2]
expected_members = set(sys.argv[3:])
root = data / "backups"
prefix = "api-startup-recovery-"

def result(code, **fields):
    print("BACKUP code=" + code + " " + " ".join(f"{k}={v}" for k, v in fields.items()))

def reject(code):
    result(code)
    raise SystemExit(1)

def validate_parent_chain(path, stop):
    current = path.parent
    while current != stop:
        info = current.lstat()
        if not stat.S_ISDIR(info.st_mode):
            reject("MEMBER_PARENT_SHAPE")
        current = current.parent

def safe_open(path, max_bytes):
    validate_parent_chain(path, root)
    before = path.lstat()
    if not stat.S_ISREG(before.st_mode) or before.st_nlink != 1 or before.st_size > max_bytes:
        reject("MEMBER_SHAPE")
    fd = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_CLOEXEC", 0))
    opened = os.fstat(fd)
    if (not stat.S_ISREG(opened.st_mode) or opened.st_nlink != 1 or
            (opened.st_dev, opened.st_ino, opened.st_size, opened.st_mtime_ns, opened.st_ctime_ns) !=
            (before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns, before.st_ctime_ns)):
        os.close(fd)
        reject("MEMBER_RACE")
    return fd, opened

try:
    if not re.fullmatch(r"[0-9a-f]{40}", expected):
        reject("EXPECTED_SHA_INVALID")
    data_info = data.lstat()
    root_info = root.lstat()
    if not stat.S_ISDIR(data_info.st_mode):
        reject("DATA_SHAPE")
    if not stat.S_ISDIR(root_info.st_mode):
        reject("ROOT_SHAPE")
    candidates = []
    with os.scandir(root) as entries:
        scanned = 0
        for entry in entries:
            scanned += 1
            if scanned > 1024:
                reject("CANDIDATE_SCAN_LIMIT")
            if entry.name.startswith(prefix):
                candidates.append(Path(entry.path))
                if len(candidates) > 64:
                    reject("CANDIDATE_LIMIT")
    candidates.sort(key=lambda p: p.name)
    if len(candidates) > 64:
        reject("CANDIDATE_LIMIT")
    matching_backups = []
    for directory in candidates:
        info = directory.lstat()
        if not stat.S_ISDIR(info.st_mode):
            reject("CANDIDATE_SHAPE")
        metadata = directory / "RECOVERY-METADATA"
        meta_info = metadata.lstat()
        if not stat.S_ISREG(meta_info.st_mode) or meta_info.st_nlink != 1 or meta_info.st_size > 512:
            reject("METADATA_SHAPE")
        meta_fd, meta_opened = safe_open(metadata, 512)
        try:
            raw = os.read(meta_fd, 513)
            meta_after = os.fstat(meta_fd)
            if len(raw) != meta_opened.st_size or (meta_after.st_dev, meta_after.st_ino, meta_after.st_size, meta_after.st_mtime_ns, meta_after.st_ctime_ns) != (meta_opened.st_dev, meta_opened.st_ino, meta_opened.st_size, meta_opened.st_mtime_ns, meta_opened.st_ctime_ns):
                reject("METADATA_RACE")
        finally:
            os.close(meta_fd)
        text = raw.decode("ascii")
        values = {}
        for line in text.splitlines():
            if "=" not in line:
                reject("METADATA_FORMAT")
            key, value = line.split("=", 1)
            if key not in {"expected_deployed_sha", "target_commit_sha", "created_utc"} or key in values:
                reject("METADATA_FORMAT")
            values[key] = value
        if set(values) != {"expected_deployed_sha", "target_commit_sha", "created_utc"} or not all(re.fullmatch(r"[0-9a-f]{40}", values[k]) for k in ("expected_deployed_sha", "target_commit_sha")):
            reject("METADATA_FORMAT")
        if not re.fullmatch(r"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ", values["created_utc"]):
            reject("METADATA_FORMAT")
        if values["target_commit_sha"] != expected:
            continue
        manifest = directory / "SHA256SUMS"
        mi = manifest.lstat()
        if not stat.S_ISREG(mi.st_mode) or mi.st_nlink != 1 or mi.st_size > 32768:
            reject("MANIFEST_SHAPE")
        manifest_fd, manifest_opened = safe_open(manifest, 32768)
        try:
            manifest_bytes = os.read(manifest_fd, 32769)
            manifest_after = os.fstat(manifest_fd)
            if len(manifest_bytes) != manifest_opened.st_size or (manifest_after.st_dev, manifest_after.st_ino, manifest_after.st_size, manifest_after.st_mtime_ns, manifest_after.st_ctime_ns) != (manifest_opened.st_dev, manifest_opened.st_ino, manifest_opened.st_size, manifest_opened.st_mtime_ns, manifest_opened.st_ctime_ns):
                reject("MANIFEST_RACE")
        finally:
            os.close(manifest_fd)
        lines = manifest_bytes.decode("ascii").splitlines()
        if not lines or len(lines) > 128:
            reject("MANIFEST_LIMIT")
        seen = set()
        required_seen = set()
        total = 0
        for line in lines:
            match = re.fullmatch(r"([0-9a-f]{64})  ([A-Za-z0-9._/-]{1,256})", line)
            if not match:
                reject("MANIFEST_FORMAT")
            digest, relative = match.groups()
            rel = Path(relative)
            if rel.is_absolute() or any(part in ("", ".", "..") for part in rel.parts) or relative in seen:
                reject("MANIFEST_PATH")
            seen.add(relative)
            if relative in expected_members:
                required_seen.add(relative)
            elif not any(relative in {name + suffix for suffix in ("-wal", "-shm", "-journal")} for name in expected_members if name.endswith(".db")):
                reject("MANIFEST_MEMBERSHIP")
            target = directory / rel
            fd, fi = safe_open(target, 4 * 1024 * 1024 * 1024)
            total += fi.st_size
            if total > 4 * 1024 * 1024 * 1024:
                os.close(fd)
                reject("BACKUP_SIZE_LIMIT")
            h = hashlib.sha256()
            try:
                with os.fdopen(fd, "rb", closefd=False) as stream:
                    for block in iter(lambda: stream.read(1024 * 1024), b""):
                        h.update(block)
                after = os.fstat(fd)
                if (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns, after.st_ctime_ns) != (fi.st_dev, fi.st_ino, fi.st_size, fi.st_mtime_ns, fi.st_ctime_ns):
                    reject("MEMBER_RACE")
            finally:
                os.close(fd)
            if h.hexdigest() != digest:
                reject("HASH_MISMATCH")
        if not expected_members.issubset(required_seen):
            reject("MEMBERSHIP_INCOMPLETE")
        matching_backups.append((len(lines), total))
    if not matching_backups:
        reject("MATCH_COUNT")
    result("VERIFIED", matches=len(matching_backups), files=sum(m[0] for m in matching_backups), bytes=sum(m[1] for m in matching_backups))
except SystemExit:
    raise
except Exception:
    reject("READ_ONLY_VALIDATION_FAILED")
PY
}

horse_id_migration_sanitize_startup_logs() {
  local python_command=${HRP_PYTHON3:-python3}
  "$python_command" -c 'import re,sys
try:
 data=sys.stdin.buffer.read(262145)
 if len(data)>262144: print("STARTUP_LOGS code=SANITIZER_LIMIT"); sys.exit(1)
 text=data.decode("utf-8",errors="strict")
 allowed=("InvalidDataException","SqliteException","IOException","UnauthorizedAccessException","InvalidOperationException","FormatException","ArgumentException","JsonException")
 counts={name:len(re.findall(r"\b"+re.escape(name)+r"\b",text)) for name in allowed}
 errors=",".join(f"{name}:{count}" for name,count in counts.items() if count) or "none"
 sqlite=sorted(set(re.findall(r"(?:SQLite Error|SqliteErrorCode|SqliteExtendedErrorCode)[^0-9]{0,8}([0-9]{1,3})",text)))
 print("STARTUP_LOGS exceptions="+errors+" sqlite_codes="+(",".join(sqlite) if sqlite else "none"))
except Exception:
 print("STARTUP_LOGS code=SANITIZER_FAILED"); sys.exit(1)'
}

horse_id_migration_cleanup_container() {
  if [[ -n "$HRP_MIGRATION_CONTAINER_NAME" ]]; then
    timeout --signal=TERM --kill-after="${HRP_MIGRATION_CHILD_GRACE_SECONDS}s" 30s \
      docker rm -f "$HRP_MIGRATION_CONTAINER_NAME" >/dev/null 2>&1 || true
    HRP_MIGRATION_CONTAINER_NAME=
  fi
}

horse_id_migration_run_fixed_command() {
  local operation_id=${1:?operation GUID is required}
  local image=${2:?image is required}
  local env_file=${3:?env file is required}
  local wait_status exit_code
  horse_id_migration_validate_guid "$operation_id" || {
    echo "Migration command requires one canonical operation GUID." >&2
    return 1
  }
  horse_id_migration_check_image_capability "$image" || return 1
  HRP_MIGRATION_CONTAINER_NAME="hrp-horse-id-migration-${operation_id//-/}-$$-$RANDOM"
  trap horse_id_migration_cleanup_container EXIT HUP INT TERM
  timeout --signal=TERM --kill-after="${HRP_MIGRATION_CHILD_GRACE_SECONDS}s" 60s \
    docker compose --env-file "$env_file" run -d --no-deps --name "$HRP_MIGRATION_CONTAINER_NAME" \
      api horse-id-migration "$operation_id" >/dev/null || {
        horse_id_migration_cleanup_container
        echo "Could not start the bounded fixed migration command." >&2
        return 1
      }
  if ! wait_status=$(timeout --signal=TERM --kill-after="${HRP_MIGRATION_CHILD_GRACE_SECONDS}s" \
      "${HRP_MIGRATION_CLI_TIMEOUT_SECONDS}s" docker wait "$HRP_MIGRATION_CONTAINER_NAME"); then
    horse_id_migration_cleanup_container
    echo "Fixed migration command timed out or could not be observed; API stays stopped." >&2
    return 1
  fi
  exit_code=$(timeout --signal=TERM --kill-after="${HRP_MIGRATION_CHILD_GRACE_SECONDS}s" 30s \
    docker inspect --format '{{.State.ExitCode}}' "$HRP_MIGRATION_CONTAINER_NAME") || {
      horse_id_migration_cleanup_container
      echo "Fixed migration command exit could not be confirmed." >&2
      return 1
    }
  horse_id_migration_cleanup_container
  if [[ "$wait_status" != "$exit_code" || "$exit_code" != "0" ]]; then
    echo "Fixed migration command did not verify a successful operation; API stays stopped." >&2
    return 1
  fi
}

horse_id_migration_copy_store_set() {
  local data_directory=${1:?data directory is required}
  local backup_directory=${2:?backup directory is required}
  local required=${3:-yes}
  shift 2
  shift 1
  local store source relative suffix before after
  local -a suffixes
  local expected_files=()
  local manifest="$backup_directory/SHA256SUMS"
  mkdir -- "$backup_directory"
  chmod 700 -- "$backup_directory"
  : > "$manifest"
  for store in "$@"; do
    source="$data_directory/$store"
    if [[ ! -e "$source" ]]; then
      if [[ "$required" == "yes" ]]; then
        echo "Configured migration store is missing: $store" >&2
        return 1
      fi
      continue
    fi
    if [[ "$store" == *.db ]]; then suffixes=("" -wal -shm -journal); else suffixes=(""); fi
    for suffix in "${suffixes[@]}"; do
      relative="$store$suffix"
      source="$data_directory/$relative"
      [[ ! -e "$source" ]] && continue
      [[ ! -L "$source" && -f "$source" ]] || {
        echo "Configured store sidecar is not a regular file: $relative" >&2
        return 1
      }
      if [[ "$suffix" == "-wal" && -s "$source" ]]; then
        echo "Uncheckpointed SQLite WAL remains; refusing incomplete backup." >&2
        return 1
      fi
      if [[ "$suffix" == "-journal" && -s "$source" ]]; then
        echo "Hot SQLite rollback journal remains; refusing incomplete backup." >&2
        return 1
      fi
      before=$(sha256sum -- "$source" | cut -d' ' -f1) || return 1
      mkdir -p -- "$backup_directory/$(dirname "$relative")"
      cp -- "$source" "$backup_directory/$relative" || return 1
      after=$(sha256sum -- "$backup_directory/$relative" | cut -d' ' -f1) || return 1
      [[ "$before" == "$after" ]] || {
        echo "Database backup verification failed for $relative." >&2
        return 1
      }
      printf '%s  %s\n' "$after" "$relative" >> "$manifest"
      expected_files+=("$relative")
    done
  done
  (
    cd "$backup_directory"
    sha256sum --check --status SHA256SUMS
  ) || {
    echo "Coherent migration backup manifest did not verify." >&2
    return 1
  }
  diff -u <(printf '%s\n' "${expected_files[@]}" | sort) \
    <(awk '{ print $2 }' "$manifest" | sort) >/dev/null || {
      echo "Migration backup manifest membership differs from the configured stores and present sidecars." >&2
      return 1
    }
}

horse_id_migration_assert_quiescent_stores() {
  local data_directory=${1:?data directory is required}
  shift
  local store suffix sidecar
  for store in "$@"; do
    for suffix in -wal -journal; do
      sidecar="$data_directory/$store$suffix"
      if [[ -s "$sidecar" ]]; then
        echo "Non-empty SQLite $suffix remains for $store; refusing API startup." >&2
        return 1
      fi
    done
  done
}
