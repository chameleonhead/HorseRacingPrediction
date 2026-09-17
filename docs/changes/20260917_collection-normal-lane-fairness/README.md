# Collection normal-lane fairness

Status: Proposed

## Problem

Production recovery for `discovery:2026091306` and the affected horse profile is blocked even though the
pipeline is active. The dispatcher always ranks Realtime before Normal. Its four-Realtime fairness escape
only activates when a Background candidate exists and then forces Background. With a persistent Realtime
backlog, a due Normal task is therefore never selected.

Production evidence on 2026-09-17 showed one running task, 1,584 waiting tasks, zero attention items, and
Realtime priority-70 jockey profiles repeatedly returning the expected `公開待ち` outcome. A sampled jockey
advanced from attempt 16 to attempt 21 while both approved recovery targets remained unchanged in Normal.
This is scheduling starvation, not a recurrence of `iv_h_name` or the Nakayama navigation failure.

## Decision

Keep Realtime preferred, but after at most four consecutive Realtime envelopes require one due
non-Realtime envelope. Within that fairness slot, select between Normal and Background using the existing
effective priority, availability time, creation time, and stable task ID ordering instead of the fixed lane
rank. This preserves the fast path and existing priority/aging rules while allowing both non-Realtime lanes
to make progress.

No task is cancelled, reclassified, or manually promoted. Retry classification, global safety stop,
per-definition schedules, and collection handlers are unchanged.

## Acceptance criteria

| ID | Observable criterion | Verification | State |
| --- | --- | --- | --- |
| AC1 | With continuously due Realtime and Normal candidates, a Normal candidate is selected no later than the fifth envelope. | Allocator unit test and dispatcher integration test | Proposed |
| AC2 | With continuously due Realtime and Background candidates, the existing Background anti-starvation behavior remains. | Existing and expanded allocator/dispatcher tests | Proposed |
| AC3 | When Normal and Background are both due in a fairness slot, selection uses effective priority, then availability, creation, and task ID; repeated dispatches cannot be dominated solely by lane rank. | Deterministic mixed-lane tests | Proposed |
| AC4 | Before the fairness threshold, Realtime remains preferred; within a selected lane, existing priority and age behavior remains unchanged. | Regression tests | Proposed |
| AC5 | Retry classification, publication-wait scheduling, global pause behavior, handlers, and task lane assignments are unchanged. | Focused collection tests and diff review | Proposed |
| AC6 | Formatting, Release build, all non-external tests, CodeGraph sync, record validation, diff/status, and secret checks pass. | Repository gates | Proposed |
| AC7 | After deployment, Normal-lane completions advance while Realtime work remains queued; the affected horse attempt runs without `iv_h_name`, and `discovery:2026091306` reaches a successful terminal state without the Nakayama error. | Deployment and production job screens | Proposed |

## Task plan

| ID | Task | Owner | Depends on | Write scope | Verification | State |
| --- | --- | --- | --- | --- | --- | --- |
| T1 | Confirm production starvation and scheduling call path. | Main | - | Read-only and this record | Production UI and CodeGraph | Verified |
| T2 | Implement bounded non-Realtime fairness with deterministic mixed-lane ordering. | Main | Approval | Allocator only | Unit tests | Dependent |
| T3 | Add dispatcher-level mixed-lane regression coverage. | Main | T2 | API tests | Focused integration tests | Dependent |
| T4 | Run repository gates, self-review, update docs, commit, and push. | Main | T2-T3 | Tests/docs/derived graph | AC6 | Dependent |
| T5 | Deploy and complete the two pending production recovery observations. | Main | T4 | Approved production recovery operations | AC7 | Dependent |

## Review gates

- **Design and task-split review — 2026-09-17, reviewer: Main.** CodeGraph and production evidence confirm
  that the blockage is in the shared allocator, not the deployed horse-field readiness logic. The proposed
  change is confined to selection of an already-due envelope. It neither broadens retries nor weakens safety
  stops. Implementation remains serialized because allocator semantics, dispatcher persistence, deployment,
  and production recovery form one shared-state path.
- **Pre-implementation review:** pending explicit approval of AC1-AC7.
- **Checkpoint review:** pending.
- **Final review:** pending.

## Verification record

- 2026-09-17: production changed from 1,586 to 1,584 waiting and from 1,348 to 1,351 recently completed,
  confirming that execution is active. The two Normal recovery resources did not receive new attempts.
- 2026-09-17: sampled Realtime jockey work returned `公開待ち` in attempts 17-21 at intervals while remaining
  eligible, confirming a persistent higher-ranked lane rather than a stopped pipeline.
- 2026-09-17: source trace confirmed `CollectionLaneAllocator.Select` orders Realtime before Normal and only
  forces Background after the consecutive-Realtime threshold.
