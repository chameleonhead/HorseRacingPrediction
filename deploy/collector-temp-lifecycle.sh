#!/bin/sh

# Own only this subtree. Never enumerate or remove arbitrary /tmp entries.
COLLECTOR_TEMP_ROOT="${COLLECTOR_TEMP_ROOT:-/tmp/horse-racing-prediction-collector}"
COLLECTOR_TEMP_MARKER='horse-racing-prediction-collector-temp-v1'

collector_temp_validate_root() {
  case "$COLLECTOR_TEMP_ROOT" in
    /*) ;;
    *) return 1 ;;
  esac
  [ "$COLLECTOR_TEMP_ROOT" != "/" ] || return 1
  [ "$COLLECTOR_TEMP_ROOT" != "/tmp" ] || return 1
  [ ! -L "$COLLECTOR_TEMP_ROOT" ] || return 1
}

collector_temp_is_owned_child() {
  candidate="$1"
  case "$candidate" in
    "$COLLECTOR_TEMP_ROOT"/invocation.*) ;;
    *) return 1 ;;
  esac
  [ "${candidate%/*}" = "$COLLECTOR_TEMP_ROOT" ] || return 1
  [ -d "$candidate" ] || return 1
  [ ! -L "$candidate" ] || return 1
  marker="$candidate/.hrp-collector-owned"
  [ -f "$marker" ] || return 1
  [ ! -L "$marker" ] || return 1
  [ "$(cat "$marker" 2>/dev/null)" = "$COLLECTOR_TEMP_MARKER" ] || return 1
}

collector_temp_is_active() {
  active_file="$1/.active-pid"
  [ -f "$active_file" ] || return 1
  [ ! -L "$active_file" ] || return 0
  active_lease="$(cat "$active_file" 2>/dev/null || true)"
  set -- $active_lease
  [ "$#" -eq 2 ] || return 0
  active_pid="$1"
  active_start="$2"
  case "$active_pid" in
    ''|*[!0-9]*) return 0 ;;
  esac
  case "$active_start" in
    ''|*[!0-9]*) return 0 ;;
  esac
  kill -0 "$active_pid" 2>/dev/null || return 1
  [ -r "/proc/$active_pid/stat" ] || return 0
  current_start="$(awk '{ print $22 }' "/proc/$active_pid/stat" 2>/dev/null || true)"
  [ -n "$current_start" ] || return 0
  [ "$current_start" = "$active_start" ]
}

collector_temp_remove_owned_child() {
  candidate="$1"
  collector_temp_is_owned_child "$candidate" || return 1
  collector_temp_is_active "$candidate" && return 2
  rm -rf -- "$candidate"
}

collector_temp_cleanup_stale() {
  collector_temp_validate_root || return 1
  for candidate in "$COLLECTOR_TEMP_ROOT"/invocation.*; do
    [ -e "$candidate" ] || [ -L "$candidate" ] || continue
    collector_temp_remove_owned_child "$candidate" || cleanup_status=$?
    case "${cleanup_status:-0}" in
      0|1|2) ;;
      *) return "$cleanup_status" ;;
    esac
    cleanup_status=0
  done
}

collector_temp_initialize() {
  collector_temp_validate_root || return 1
  umask 077
  mkdir -p "$COLLECTOR_TEMP_ROOT"
  chmod 700 "$COLLECTOR_TEMP_ROOT"
  collector_temp_cleanup_stale
}

collector_temp_begin_invocation() {
  collector_temp_validate_root || return 1
  invocation_dir="$(mktemp -d "$COLLECTOR_TEMP_ROOT/invocation.XXXXXX")" || return 1
  printf '%s\n' "$COLLECTOR_TEMP_MARKER" >"$invocation_dir/.hrp-collector-owned"
  process_start="$(awk '{ print $22 }' "/proc/$$/stat" 2>/dev/null || true)"
  if [ -z "$process_start" ]; then
    rm -rf -- "$invocation_dir"
    return 1
  fi
  printf '%s %s\n' "$$" "$process_start" >"$invocation_dir/.active-pid"
  mkdir -p "$invocation_dir/home" "$invocation_dir/cache" "$invocation_dir/config" "$invocation_dir/tmp"
  printf '%s\n' "$invocation_dir"
}

collector_temp_finish_invocation() {
  invocation_dir="$1"
  collector_temp_is_owned_child "$invocation_dir" || return 1
  rm -f -- "$invocation_dir/.active-pid"
  collector_temp_remove_owned_child "$invocation_dir"
}

collector_temp_log_metrics() {
  phase="$1"
  owned_kib="$(du -sk "$COLLECTOR_TEMP_ROOT" 2>/dev/null | awk 'NR == 1 { print $1 }')"
  free_kib="$(df -Pk "$COLLECTOR_TEMP_ROOT" 2>/dev/null | awk 'NR == 2 { print $4 }')"
  free_inodes="$(df -Pi "$COLLECTOR_TEMP_ROOT" 2>/dev/null | awk 'NR == 2 { print $4 }')"
  child_count="$(find "$COLLECTOR_TEMP_ROOT" -mindepth 1 -maxdepth 1 -type d -name 'invocation.*' 2>/dev/null | wc -l | tr -d ' ')"
  printf 'Collector temp storage. Phase=%s OwnedKiB=%s FreeKiB=%s FreeInodes=%s InvocationDirectories=%s\n' \
    "$phase" "${owned_kib:-Unavailable}" "${free_kib:-Unavailable}" \
    "${free_inodes:-Unavailable}" "${child_count:-Unavailable}"
}

collector_temp_log_diagnostic() {
  phase="$1"
  invocation_dir="$2"
  group_alive="$3"
  invocation_kib=0
  if [ -n "$invocation_dir" ] && collector_temp_is_owned_child "$invocation_dir"; then
    invocation_kib="$(du -sk "$invocation_dir" 2>/dev/null | awk 'NR == 1 { print $1 }')"
  fi
  owned_kib="$(du -sk "$COLLECTOR_TEMP_ROOT" 2>/dev/null | awk 'NR == 1 { print $1 }')"
  free_kib="$(df -Pk "$COLLECTOR_TEMP_ROOT" 2>/dev/null | awk 'NR == 2 { print $4 }')"
  free_inodes="$(df -Pi "$COLLECTOR_TEMP_ROOT" 2>/dev/null | awk 'NR == 2 { print $4 }')"
  case "$group_alive" in
    true|false) ;;
    *) group_alive=unknown ;;
  esac
  printf 'Collector temp diagnostic. Phase=%s InvocationKiB=%s OwnedKiB=%s FreeKiB=%s FreeInodes=%s GroupAlive=%s\n' \
    "$phase" "${invocation_kib:-Unavailable}" "${owned_kib:-Unavailable}" \
    "${free_kib:-Unavailable}" "${free_inodes:-Unavailable}" "$group_alive"
}
