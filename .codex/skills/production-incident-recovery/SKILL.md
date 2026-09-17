---
name: production-incident-recovery
description: Diagnose and recover production sites, collection jobs, pipelines, and deployed services when the user asks to restore, recover, resume, or fix an outage or recurring operational error. Use for live incident recovery; do not use for ordinary local debugging without production impact.
---

# Production Incident Recovery

Restore service safely while keeping temporary recovery separate from root-cause closure.

## Workflow

1. Capture the current failure signal before mutation when it is available: service/pipeline state, failing resource or task, error type and message, occurrence time, retry count, request/final URL, deployment revision, and affected scope. Do not delay an urgent reversible stabilization when the evidence is already durable.
2. Stabilize with the narrowest reversible action authorized by the user. Isolate a failing item before resuming unrelated work when the system supports it. Do not bulk-retry deterministic failures or erase queues, failure records, or diagnostics merely to make the dashboard green.
3. Verify stabilization through an observable production signal such as health, an active worker, decreasing queue depth, a succeeding request, or absence of immediate re-stop. Record the observation window. Label this result `temporary recovery`; it is not incident closure.
4. Trace the root cause through the real path that produced the incident: trigger/input, persisted state, dispatch, worker/handler, external adapter, completion classification, retry/stop policy, and operator surface. Separate:
   - the external or input condition;
   - the technical defect that converted it into an outage;
   - the workflow or test gap that allowed the defect to ship.
5. Produce a durable corrective proposal with affected scope, safety boundaries, observable acceptance criteria, regression tests, deployment steps, and production verification. Follow `document-driven-development` when repository behavior or operations must change. Do not edit production code before its approval gate.
6. After approval, implement and verify the permanent correction. Reproduce the production-shaped failure in a regression test, verify the unaffected safety behavior, deploy, recover only eligible failed work, and observe the production path through its terminal outcome.

## Completion semantics

- If only a pause/resume, restart, retry, failover, or manual isolation was performed, report `temporarily recovered` and continue root-cause analysis in the same task unless the user explicitly limits the request to immediate stabilization.
- Do not say `recovered`, `resolved`, `completed`, or equivalent without stating which of these dimensions are complete: stabilization, root cause, corrective design, implementation, deployment, and production verification.
- A proposed fix awaiting approval is not a permanent resolution. Present the root cause, proposal, and approval boundary explicitly.
- A code deployment is not closure until the original production-shaped failure no longer stops or corrupts the workflow and the intended work visibly progresses.
- If a durable fix is outside the user's authority or current scope, report the temporary state and the concrete remaining root-cause item instead of silently ending after restart.

## Recovery safeguards

- Preserve evidence and user data. Prefer suppression, quarantine, pause/resume, or idempotent recovery over deletion.
- Keep systemic safety stops for unknown or integrity-threatening failures. Narrow only failures proven to be isolated, and make that classification explicit in code rather than deriving it from localized message text.
- Do not retry page-structure, identity, validation, or other deterministic failures under the same implementation revision unless evidence shows the input condition changed.
- Monitor long enough to observe at least one meaningful unit of progress after stabilization; choose a window proportional to normal job duration and state what was observed.

## Incident record

Keep a compact ledger in the governing change record or incident artifact:

```text
Incident: <time and production symptom>
Temporary recovery: <action and production evidence>
Root cause: <external condition / technical defect / workflow gap>
Corrective proposal: <change record and acceptance criteria>
Permanent fix: Not started | Approved | Implemented | Deployed | Verified
Remaining risk: <specific open item or none>
```

The completion gate is a one-to-one mapping from the production symptom to evidence for temporary recovery and, unless explicitly excluded by the user, a reviewed permanent corrective path.
