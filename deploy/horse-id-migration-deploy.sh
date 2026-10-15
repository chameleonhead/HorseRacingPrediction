#!/usr/bin/env bash
# Sourceable deployment helpers for the fixed horse-identity migration workflow.
# This file intentionally has no top-level side effects.

HRP_MIGRATION_MAX_JOURNAL_BYTES=$((64 * 1024 * 1024))
HRP_MIGRATION_CLI_TIMEOUT_SECONDS=${HRP_MIGRATION_CLI_TIMEOUT_SECONDS:-$((30 * 60))}
HRP_MIGRATION_CHILD_GRACE_SECONDS=${HRP_MIGRATION_CHILD_GRACE_SECONDS:-10}
HRP_MIGRATION_CONTAINER_NAME=

horse_id_migration_validate_guid() {
  local operation_id=${1-}
  [[ "$operation_id" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]] &&
    [[ "${operation_id//-/}" != "00000000000000000000000000000000" ]]
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
