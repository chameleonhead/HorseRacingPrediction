# C10: Explicit suspension for a compatible recovery startup

Status: Approved. User approved this amendment to RC8/C9 with 「問題ないです。お願いします」. This is local implementation/verification approval, not deployment authorization.

## Evidence and concern

Actual isolated Program startup with a paused collection database and queue, producers, watchdog, DLQ and ordinary notifications disabled still created a subject Recovery request/task/outbox and updated the incident notification. Program always invokes SubjectIdentificationAutoRecovery; pipeline alerts deliberately ignore the ordinary notification switch. No schema mutation caused these writes. The previous C9 configuration-only stopped-startup assumption is false. Existing normal startup and alert contracts must not be silently redefined.

## Recommended contract

Add `CollectionPlatform:MaintenanceMode`, default `false`, read at startup only. When true, suspend all API-owned automatic collection actions: dispatcher and startup queue kick, refresh/discovery producers, task-lease Watchdog, backfill recovery, DLQ reconciliation, pipeline alert delivery, metric delivery and startup subject auto-recovery. The runtime status response still exposes eight slots, all Disabled. The normal false/default path preserves existing flags, startup recovery and always-on pipeline alerts.

This is automatic-processing suspension, not a database read-only firewall: schema compatibility initialization and definition registration remain, authenticated explicit operator actions and already-running external workers are not redefined. It must not resume the persisted pipeline, clear queued work, alter task/lease/failure history, suppress resources, mark alerts sent or rewrite migration history. Recovery procedure requires an already paused/drained environment; setting this flag alone is not evidence that external workers stopped. No production activation is authorized by local implementation approval.

## Acceptance and tests

- Default/false preserves current startup recovery and pipeline alert behavior, including the existing independent Watchdog configuration truth table.
- True prevents the two reproduced startup mutations and registers none of the API-owned automatic collection loops; all eight runtime slots report Disabled and authenticated operational reads remain available.
- A populated, paused schema25 backup restored to a distinct temporary path survives two actual compatible API restarts with exact business IDs, values, links, tasks/outbox/lease/hold/recovery state preserved. Definition/schema initialization is compared separately and never used to excuse business-row changes.
- An explicit manual operation retains its existing authorization and behavior; suspension does not masquerade as permanent data immutability or resume work.
- Existing pure-policy, integration, browser and default-configuration regression tests pass. No new timer or generic scheduling framework.

## Alternatives and residual risk

Changing ordinary notification Enabled semantics or globally making manual Recovery obey pause would break existing contracts; reject those alternatives. Continuing current startup behavior is compatible with historical behavior but cannot satisfy the proposed stopped-recovery guarantee. Reverting to unmodified schema24 code is not viable after schema25; it rejects startup. Deleting metadata or restoring an older DB over newer business data is not authorized.

Residual risk: explicit operator writes and external workers remain possible; the separately authorized operational procedure must pause/drain and verify them. Maintenance mode is visible as Disabled actions and must be explicitly removed with a compatible configuration/release before normal collection resumes; removing it does not automatically clear persisted pause.

Agent position: recommend the explicit opt-in switch, preserving normal defaults. User disposition: Approved. Concern state: Resolved in design. I4b is Verified: C10 focused14/14, populated backup/restored DB with two actual API starts, exact business preservation and separate definition comparison, actual old-binary refusal, authorized manual operations and default-false behavior all passed. Windows restart/old-binary group4/4 and final native full suite are retained in the parent evidence. This is local recovery proof, not production cutover authorization.
