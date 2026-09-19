# Compact agent execution audit

Use this reference only when work is delegated or routing effectiveness is being evaluated. The governing change record keeps one task plan; do not create a second audit ledger.

## Task-plan fields

Add three columns to the existing task plan:

- `Routing`: tier plus a short reason, for example `Worker — bounded parser fixture`.
- `Audit`: `none` for lead-only work or delegated attempt IDs such as `T2-A1`.
- `Result metrics`: `usage availability; retries N; corrections N; reviews N`.

Lead-only tasks require no JSON. Do not repeat objective, scope, verification, evidence, or state in audit prose.

## Recording events

Write audit data only at:

1. delegated dispatch — identity, route, scope, start revision, telemetry availability;
2. delegated completion — end revision/patch, outcome, retries, corrections, reviews, verification;
3. material verification failure — one failure-ledger row, closed only after the original gate passes;
4. final review — update the compact result tuple.

Ordinary successful commands, commentary, and routine state changes create no audit event.

## Compact delegated attempt

Store one JSON file per attempt under `docs/changes/<change>/agent-audits/<attempt-id>.json`:

```json
{
  "schemaVersion": 1,
  "changeId": "20260920_example",
  "taskId": "T2",
  "attemptId": "T2-A1",
  "state": "completed",
  "taskDifficulty": "low",
  "route": {
    "tier": "worker",
    "requestedModel": "runtime-default",
    "observedModel": null,
    "observationSource": null,
    "modelTelemetryReason": "runtime did not expose worker model"
  },
  "usage": {
    "availability": "unavailable",
    "totalTokens": null,
    "reason": "runtime did not expose per-worker usage"
  },
  "scope": ["path/owned/by/worker"],
  "review": {
    "usageAvailability": "unavailable",
    "totalTokens": null,
    "activeMinutes": null,
    "reason": "runtime did not expose review usage or active time"
  },
  "elapsed": {
    "availability": "unavailable",
    "minutes": null,
    "reason": "runtime did not expose reliable worker elapsed effort"
  },
  "startRevision": "commit-id",
  "endRevisionOrPatch": "patch-id",
  "outcome": {
    "verificationPassed": true,
    "qualityPassed": true,
    "scopePassed": true,
    "independentChallenge": "lead counterexample review",
    "retries": 0,
    "leadCorrections": 0,
    "reviewPasses": 1,
    "promoted": false,
    "escapedDefects": 0,
    "escalations": 0,
    "decision": "accept"
  }
}
```

At dispatch, use `state: "active"`, leave outcome, review telemetry, and elapsed telemetry values null, and never prefill completion data. Requested model describes configuration intent; do not copy it into `observedModel`. At completion, unavailable or partial model, usage, review, or elapsed telemetry uses null plus one short reason.

## Gates

- Active task write scopes may not overlap unless dependencies serialize them.
- A delegated active or verified task links every attempt JSON; dependent/unstarted tasks do not.
- `Verified` requires a completed attempt, successful verification, a decision, and no open linked material failure.
- Normal successful JSON stays below 2,500 UTF-8 bytes. Fixture task-plan audit additions stay below 20 nonblank lines.
- Do not estimate tokens, duration, model identity, or currency.
- Fewer than five comparable successful samples may inform a note but cannot change persistent routing defaults.

Validate with:

```text
python scripts/audit_agent_execution.py <change-record-path>
```
