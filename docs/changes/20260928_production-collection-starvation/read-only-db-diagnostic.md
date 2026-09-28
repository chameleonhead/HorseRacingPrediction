# Read-only production SQLite aggregate — execution not authorized

Status: Proposed for independent review. This is a prepared procedure only. It has not been run, and does not authorize SSH, production access, or any write/repair/restart/deployment action. A separate user authorization is required after review.

## Purpose and output boundary

Determine whether Ready work is missing a current-generation undispatched outbox row, whether task/outbox cardinality or generation anomalies exist, and how rows are distributed across task status, lane, definition, reservation, dispatch, and active race-repair hold state. Return counts only. No task, race, resource, request, outbox, envelope, lease, hold, queue, host, or credential identifiers; no raw rows, JSON, URLs, failure text, environment, configuration, or logs.

The capture timestamp is UTC and is produced by SQLite. The observation window is the single consistent online-backup snapshot; this query does not infer historical rates. It is a point-in-time count, not proof by itself that an anomaly caused starvation.

## Fixed source and connection

- Repository deployment layout maps the API container's `/data` to the application directory's `data` directory, with database filename `collection-platform.db`.
- Fixed host-side source path: `/opt/horse-racing-prediction/app/data/collection-platform.db`.
- Fixed SQLite URI: `file:/opt/horse-racing-prediction/app/data/collection-platform.db?mode=ro`.
- Open with the SQLite CLI's `-readonly` option and URI `mode=ro`; use a 5-second SQLite busy timeout. The remote outer timeout is 120 seconds. Subcommand budgets are 25 seconds for online backup, 5 seconds for the `query_only` check, 25 seconds for aggregate SQL, and 10 seconds for each of the two source-main hash computations. Those bounded steps total at most 75 seconds, leaving up to 45 seconds for SQLite/SSH setup, metadata checks, output, traps, and cleanup; reaching the outer timeout aborts the procedure. Do not use `immutable=1`: the API is live and the database may be in WAL mode.
- Snapshot with SQLite's online `.backup` from that read-only source connection to a newly created mode-700 temporary directory, then run the aggregate against the snapshot using a second `-readonly`/`mode=ro`/`PRAGMA query_only=ON` connection. This uses SQLite's backup API to obtain a transactionally consistent database image including committed WAL state; do not `cp` or `rsync` only the main database file, stop the service, checkpoint WAL, or change journal mode.
- Read-only access to WAL databases may require readable existing `-wal` and `-shm` sidecars. The application is expected to have them open, but their presence and permissions are a preflight gate. If the installed CLI cannot open the source as read-only or perform `.backup` without source mutation, abort. Do not install tools or switch to a live query / alternate method without a separately reviewed revision of this artifact.

## Preflight and abort conditions

After the user separately authorizes this exact read-only diagnostic, use the already configured, approved SSH alias/identity; do not copy a host name, key, token, password, `.env`, application configuration, or SSH configuration into this record or output. Run only the allowlisted read-only checks and snapshot/query below.

Abort before querying if any condition holds:

1. The approved SSH identity/host is unclear, host-key verification fails, or the operator has not authorized this exact diagnostic.
2. The fixed source path is missing, is not a regular file, is not readable, resolves outside the expected application data directory, or either existing WAL sidecar is unreadable.
3. `sqlite3` is missing, does not support `-readonly`, URI `mode=ro`, `.backup`, `.timeout`, JSON functions, or the query's SQLite syntax; or any required bounded/system utility (`timeout`, `realpath`, `stat`, `sha256sum`, `mktemp`, `date`) is missing. Do not install or upgrade tools during the incident.
4. The online backup, query-only check, aggregate SQL, or either hash computation exceeds its own hard timeout; the total procedure reaches the 120-second outer timeout; or any step reports `SQLITE_BUSY`, malformed schema, missing expected tables/columns, hold-resolver mismatch, or another error. Do not fall back to a partial/main-file copy or extend a timed-out run in place; stop, clean only the exact private snapshot, and request independent review of a revised budget/procedure.
5. Any proposed command would print database contents, identifiers, configuration, process environment, service logs, secrets, or unrestricted filesystem listings.
6. A pre/post check suggests this procedure modified a source database file or source sidecar. Preserve only the local diagnostic metadata, stop, and ask for independent review; do not attempt a repair.

If the application is actively writing during the bounded backup, SQLite provides a consistent snapshot. Source file metadata may consequently change from normal application writes between pre- and post-check; that is an inconclusive comparison, not evidence that this read-only procedure wrote to the source. The SQLite connection mode and the command transcript remain the primary no-write controls.

## Allowlisted host commands

The following is the complete command allowlist after explicit authorization. `APPROVED_LIGHTSAIL_ALIAS` is an existing SSH config alias supplied at execution time; do not replace it with or record a raw host/IP/key. Do not enable shell tracing. Do not run `docker compose config`, `docker inspect`, `env`, `printenv`, `ps` with command arguments, `journalctl`, `docker logs`, or commands that print configuration or row data.

On the approved Lightsail host, run this single bounded script. `stat` and `sha256sum` results are local no-write evidence and must not be included in the shared aggregate report. The SQL result is the only database-derived output allowed to leave the host/session.

```sh
ssh -T -o BatchMode=yes -o StrictHostKeyChecking=yes "$APPROVED_LIGHTSAIL_ALIAS" 'timeout 120s sh -s' <<'REMOTE'
set -eu
  src=/opt/horse-racing-prediction/app/data/collection-platform.db
  test -f "$src" && test -r "$src"
  test "$(realpath -e "$src")" = "$src"
  test -r "$src-wal" && test -r "$src-shm"
  sqlite3 --version
  work=$(mktemp -d /tmp/hrp-collection-readonly.XXXXXX)
  chmod 700 "$work"
  trap 'rm -f "$work/snapshot.db" "$work/snapshot.db-wal" "$work/snapshot.db-shm" "$work/snapshot.db-journal"; rmdir "$work" 2>/dev/null || true' EXIT HUP INT TERM
  before=$(stat -c "%s %Y" "$src")
  before_hash_output=$(timeout 10s sha256sum "$src")
  before_hash=${before_hash_output%% *}
  before_wal=$(stat -c "%s %Y" "$src-wal")
  before_shm=$(stat -c "%s %Y" "$src-shm")
  backup_started_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)
  timeout 25s sqlite3 -readonly -batch -bail "file:$src?mode=ro" ".timeout 5000" ".backup $work/snapshot.db"
  backup_completed_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)
  timeout 5s sqlite3 -readonly -batch -bail "file:$work/snapshot.db?mode=ro" ".timeout 5000" "PRAGMA query_only=ON; PRAGMA query_only;"
  timeout 25s sqlite3 -readonly -batch -bail -header -column "file:$work/snapshot.db?mode=ro" ".timeout 5000" "PRAGMA query_only=ON; <PASTE THE REVIEWED SQL FROM THE NEXT SECTION VERBATIM>"
  after=$(stat -c "%s %Y" "$src")
  after_hash_output=$(timeout 10s sha256sum "$src")
  after_hash=${after_hash_output%% *}
  after_wal=$(stat -c "%s %Y" "$src-wal")
  after_shm=$(stat -c "%s %Y" "$src-shm")
  printf "backup-started-utc=%s backup-completed-utc=%s source-main-before=%s source-main-after=%s source-hash-before=%s source-hash-after=%s wal-before=%s wal-after=%s shm-before=%s shm-after=%s\n" "$backup_started_utc" "$backup_completed_utc" "$before" "$after" "$before_hash" "$after_hash" "$before_wal" "$after_wal" "$before_shm" "$after_shm"
REMOTE
```

Before execution, replace only the SQL placeholder with the exact reviewed SQL below; do not alter the fixed URI, timeout, flags, output columns, or other commands. This artifact intentionally does not contain an SSH alias or secret. The script prints SQLite version and `PRAGMA query_only` (`1` expected), the count-only query, and source-file metadata/hash comparison. A missing sidecar is an abort rather than permission to create/change it. The 120-second outer budget exceeds the 75-second sum of the bounded snapshot, verification, query, and hash steps by 45 seconds for setup and cleanup; a timeout at any layer aborts rather than allowing an unbounded retry.

Allowed output for the review packet: observation UTC, backup start/completion UTC window, metric label, task status, lane, pseudonymous definition, generation match, dispatch state, reservation state, hold-match state, and integer count; plus SQLite version, query-only value, timeout/exit status, and a statement that source metadata checks were equal or changed/inconclusive. Keep exact hashes and file metadata in restricted local operator notes only. Never attach the SSH transcript if it contains anything outside this allowlist.

## Exact count-only SQL (for the snapshot only)

The query intentionally never selects an identifier, row payload, or timestamp from a table. Internal task/resource/hold IDs and raw definition IDs are used only for joins/grouping; they are not projected. Definitions are mapped to snapshot-local pseudonyms (`definition-001`, etc.), so the result counts each definition separately without revealing definition IDs. Dimensions are limited to task status, lane, pseudonymous definition, generation relation, dispatch state, reservation state, and canonical active-hold match state. No dynamic values are interpolated.

```sql
WITH
clock(now_ms) AS (
  SELECT CAST(strftime('%s','now') AS INTEGER) * 1000
),
definition_map AS (
  SELECT
    DefinitionId,
    'definition-' || printf('%03d', ROW_NUMBER() OVER (ORDER BY DefinitionId)) AS definition_label
  FROM (SELECT DISTINCT DefinitionId FROM collection_tasks)
),
active_holds AS (
  SELECT RaceId FROM race_repair_holds WHERE ReleasedAt IS NULL
),
resource_canonical AS (
  SELECT
    r.ResourcePk,
    r.Type,
    r.ResourceId,
    CASE
      WHEN json_valid(r.AttributesJson)
       AND substr(json_extract(r.AttributesJson, '$.domainRaceId'), 1, 5) = 'race-'
       AND lower(json_extract(r.AttributesJson, '$.domainRaceId')) GLOB 'race-????????-????-????-????-????????????'
       AND lower(substr(json_extract(r.AttributesJson, '$.domainRaceId'), 6)) NOT GLOB '*[^0-9a-f-]*'
       AND length(json_extract(r.AttributesJson, '$.domainRaceId')) = 41
       AND r.ResourceId GLOB 'race-????????-????-????-????-????????????'
       AND substr(r.ResourceId, 6) NOT GLOB '*[^0-9a-f-]*'
       AND length(r.ResourceId) = 41
       AND json_extract(r.AttributesJson, '$.domainRaceId') <> r.ResourceId
        THEN NULL
      WHEN json_valid(r.AttributesJson)
       AND substr(json_extract(r.AttributesJson, '$.domainRaceId'), 1, 5) = 'race-'
       AND lower(json_extract(r.AttributesJson, '$.domainRaceId')) GLOB 'race-????????-????-????-????-????????????'
       AND lower(substr(json_extract(r.AttributesJson, '$.domainRaceId'), 6)) NOT GLOB '*[^0-9a-f-]*'
       AND length(json_extract(r.AttributesJson, '$.domainRaceId')) = 41
        THEN json_extract(r.AttributesJson, '$.domainRaceId')
      WHEN r.ResourceId GLOB 'race-????????-????-????-????-????????????'
       AND substr(r.ResourceId, 6) NOT GLOB '*[^0-9a-f-]*'
       AND length(r.ResourceId) = 41
        THEN r.ResourceId
      ELSE NULL
    END AS canonical_race_id
  FROM collection_resources AS r
),
resource_hold_state AS (
  SELECT
    rc.ResourcePk,
    CASE
      WHEN rc.Type NOT IN ('Race','RaceCard','RaceResult','RaceOdds')
        OR rc.ResourceId GLOB 'backfill:[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'
        OR rc.ResourceId GLOB 'recollection:[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'
        OR rc.ResourceId GLOB 'discovery:[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'
        THEN 'not-applicable'
      WHEN rc.canonical_race_id IS NOT NULL
       AND EXISTS (SELECT 1 FROM active_holds h WHERE h.RaceId = rc.canonical_race_id)
        THEN 'held'
      WHEN rc.canonical_race_id IS NOT NULL THEN 'not-held'
      WHEN EXISTS (SELECT 1 FROM active_holds) THEN 'unresolved-active-hold-present'
      ELSE 'unresolved-no-active-hold'
    END AS hold_match_state
  FROM resource_canonical AS rc
),
task_outbox_counts AS (
  SELECT
    t.TaskId,
    CASE WHEN t.Status IN ('Pending','Ready','Running','RetryWaiting','WaitingDiscovery','Succeeded','Failed','Cancelled','DeadLetter') THEN t.Status ELSE 'OTHER' END AS task_status,
    CASE WHEN t.Lane IN ('Realtime','Normal','Background') THEN t.Lane ELSE 'OTHER' END AS lane,
    COALESCE(dm.definition_label, 'ORPHAN') AS definition_label,
    COUNT(o.OutboxId) AS total_outbox_count,
    SUM(CASE WHEN o.OutboxId IS NOT NULL
                  AND o.DispatchedAt IS NULL
                  AND o.DispatchGeneration = t.DispatchGeneration
             THEN 1 ELSE 0 END) AS current_undispatched_count
  FROM collection_tasks AS t
  LEFT JOIN collection_task_outbox AS o ON o.TaskId = t.TaskId
  LEFT JOIN definition_map AS dm ON dm.DefinitionId = t.DefinitionId
  GROUP BY t.TaskId, t.Status, t.Lane, dm.definition_label
),
outbox_dimensions AS (
  SELECT
    CASE WHEN t.TaskId IS NULL THEN 'ORPHAN'
         WHEN t.Status IN ('Pending','Ready','Running','RetryWaiting','WaitingDiscovery','Succeeded','Failed','Cancelled','DeadLetter') THEN t.Status ELSE 'OTHER' END AS task_status,
    CASE WHEN t.TaskId IS NULL THEN 'ORPHAN'
         WHEN t.Lane IN ('Realtime','Normal','Background') THEN t.Lane ELSE 'OTHER' END AS lane,
    COALESCE(dm.definition_label, 'ORPHAN') AS definition_label,
    CASE
      WHEN t.TaskId IS NULL THEN 'orphan-task'
      WHEN o.DispatchGeneration = t.DispatchGeneration THEN 'current'
      ELSE 'stale'
    END AS generation_match,
    CASE WHEN o.DispatchedAt IS NULL THEN 'undispatched' ELSE 'dispatched' END AS dispatch_state,
    CASE
      WHEN o.DispatchedAt IS NOT NULL THEN 'not-applicable-dispatched'
      WHEN o.ReservationToken IS NULL AND o.ReservedUntilUnixMilliseconds IS NULL THEN 'unreserved'
      WHEN o.ReservationToken IS NULL THEN 'inconsistent-token-missing'
      WHEN o.ReservedUntilUnixMilliseconds IS NULL THEN 'inconsistent-expiry-missing'
      WHEN o.ReservedUntilUnixMilliseconds > (SELECT now_ms FROM clock) THEN 'active'
      ELSE 'expired'
    END AS reservation_state,
    COALESCE(rhs.hold_match_state, 'unknown-resource') AS hold_match_state
  FROM collection_task_outbox AS o
  LEFT JOIN collection_tasks AS t ON t.TaskId = o.TaskId
  LEFT JOIN resource_hold_state AS rhs ON rhs.ResourcePk = t.ResourcePk
  LEFT JOIN definition_map AS dm ON dm.DefinitionId = t.DefinitionId
),
aggregate_rows AS (
  SELECT
    'outbox' AS metric,
    task_status,
    lane,
    definition_label,
    generation_match,
    dispatch_state,
    reservation_state,
    hold_match_state,
    COUNT(*) AS count
  FROM outbox_dimensions
  GROUP BY task_status, lane, definition_label, generation_match, dispatch_state, reservation_state, hold_match_state

  UNION ALL

  SELECT
    'ready_without_current_generation_undispatched_outbox',
    task_status, lane, definition_label,
    'missing-current-undispatched', '', '', '', COUNT(*)
  FROM task_outbox_counts
  WHERE task_status = 'Ready' AND current_undispatched_count = 0
  GROUP BY task_status, lane, definition_label

  UNION ALL

  SELECT
    'tasks_without_any_outbox',
    task_status, lane, definition_label,
    'no-outbox', '', '', '', COUNT(*)
  FROM task_outbox_counts
  WHERE total_outbox_count = 0
  GROUP BY task_status, lane, definition_label

  UNION ALL

  SELECT
    'multiple_current_generation_undispatched_outboxes',
    task_status, lane, definition_label,
    'duplicate-current-undispatched', '', '', '', COUNT(*)
  FROM task_outbox_counts
  WHERE current_undispatched_count > 1
  GROUP BY task_status, lane, definition_label

  UNION ALL

  SELECT
    'execution_lease',
    CASE WHEN Status IN ('StartPending','Running') THEN Status ELSE 'terminal-or-other' END, '', '', '', '', '', '', COUNT(*)
  FROM collection_execution_leases
  GROUP BY CASE WHEN Status IN ('StartPending','Running') THEN Status ELSE 'terminal-or-other' END

  UNION ALL

  SELECT
    'race_repair_hold',
    CASE WHEN ReleasedAt IS NULL THEN 'active' ELSE 'released' END,
    '', '', '', '', '', '', COUNT(*)
  FROM race_repair_holds
  GROUP BY CASE WHEN ReleasedAt IS NULL THEN 'active' ELSE 'released' END

  UNION ALL

  SELECT
    'pipeline_control',
    CASE WHEN IsPaused = 1 THEN 'paused' ELSE 'unpaused' END,
    '', '', '', '', '', '', COUNT(*)
  FROM collection_platform_controls
  GROUP BY CASE WHEN IsPaused = 1 THEN 'paused' ELSE 'unpaused' END
)
SELECT
  strftime('%Y-%m-%dT%H:%M:%fZ','now') AS observed_utc,
  metric,
  task_status,
  lane,
  definition_label AS definition,
  generation_match,
  dispatch_state,
  reservation_state,
  hold_match_state,
  count
FROM aggregate_rows
ORDER BY metric, task_status, lane, definition_label, generation_match, dispatch_state, reservation_state, hold_match_state;
```

Expected output is zero or more aggregate rows, including a row for every observed outbox category by `hold_match_state` (`held`, `not-held`, not-applicable, or unresolved with/without any active hold), active execution-lease state (other states combined), repair-hold active/released state, control pause state, Ready task with no current-generation undispatched outbox, task with no outbox, and task with duplicate current-generation undispatched outboxes. Absence of an anomaly group means its count is zero, not that the query failed. Task status and lane are allowlisted enums; unexpected values are mapped to `OTHER`. Definition IDs are always replaced by snapshot-local pseudonyms before output. The hold dimension joins the canonical race resource identity to active `race_repair_holds`, never projecting either identifier. Unresolved rows are explicitly reported, not counted as definitely held/not-held. Before execution, independent source review must confirm production uses the fallback canonicalization represented above and that all resource-ID canonicalization cases are represented; if an injected race identity resolver, an unrepresented deterministic mapping, or a conflicting domainRaceId/resource ID is active, abort and revise the SQL to match that resolver rather than claiming the hold counts are exact.

## No-write evidence, cleanup, and independent review

- The source is opened twice with both SQLite URI `mode=ro` and CLI `-readonly`; no `INSERT`, `UPDATE`, `DELETE`, schema change, `VACUUM`, checkpoint, or journal-mode pragma exists in the procedure. `PRAGMA query_only` must print `1` for the snapshot query connection.
- Capture source main-file size/mtime/hash and WAL/SHM size/mtime immediately before and after the backup/query. Equal observations strengthen the no-write record. If values differ, the live application may have written concurrently; mark the comparison inconclusive and do not attribute that difference to this query. Read-only settings and the exact command/exit status remain required evidence.
- The only intended write is the temporary SQLite backup under a newly created private `/tmp/hrp-collection-readonly.*` directory. The shell trap removes that exact snapshot and its journal/WAL/SHM sidecars plus directory on normal exit or handled signal. If timeout/host interruption prevents cleanup, remove only the exact recorded temporary directory after verifying it is under `/tmp` and begins with `hrp-collection-readonly.`; never use a wildcard deletion.
- Rollback: not applicable to source because no source write or service mutation is part of this procedure. Temporary snapshot cleanup is the only cleanup action. If source modification is suspected, stop and escalate; do not restore, checkpoint, or otherwise modify production data.
- Independent reviewer must check: authorization scope; source path matches deployment mapping; SQLite CLI version/readonly URI and WAL sidecars; exact SQL against current EF schema; canonical race-resource resolver equivalence with active-hold matching; no identifiers in projections; aggregate-only output; outer budget greater than the bounded backup/query/hash steps with margin; timeout and abort rules; no secret/config/log command; source pre/post evidence; temp-path cleanup; and final output redaction. Record reviewer and result outside production output before SSH execution.
- At completion, retain only the reviewed count-only result, UTC observation timestamp, timeout/exit status, and no-write evidence classification in the incident record. Do not retain the database snapshot or raw SSH transcript.
