# Collection normal-lane fairness

- Status: Approved
- Owner: Main
- Created: 2026-09-17
- Updated: 2026-09-17

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

Use a bounded weighted rotation. When all lanes remain due, dispatch four Realtime envelopes, one Normal
envelope, four Realtime envelopes, and one Background envelope. The steady-state allocation is therefore
80% Realtime, 10% Normal, and 10% Background. When Realtime is empty, alternate Normal and Background 1:1.
When any lane is empty, its slot is work-conserving and is immediately available to the other due lanes;
capacity is never left idle. Within the selected lane, retain the existing effective priority, availability
time, creation time, and stable task ID ordering.

No task is cancelled, reclassified, or manually promoted. Retry classification, global safety stop,
per-definition schedules, and collection handlers are unchanged.

## Acceptance criteria

| ID | Observable criterion | Verification | State |
| --- | --- | --- | --- |
| AC1 | With continuously due Realtime and Normal candidates, a Normal candidate is selected no later than the fifth envelope. | Allocator unit test and dispatcher integration test | Verified |
| AC2 | With continuously due Realtime and Background candidates, the existing Background anti-starvation behavior remains. | Existing and expanded allocator/dispatcher tests | Verified |
| AC3 | With all lanes continuously due, the stable rotation is four Realtime, one Normal, four Realtime, one Background (80%/10%/10%); with no Realtime it alternates Normal and Background 1:1. | Deterministic mixed-lane sequence tests | Verified |
| AC4 | Before the fairness threshold, Realtime remains preferred; within a selected lane, existing priority and age behavior remains unchanged. | Regression tests | Verified |
| AC5 | Retry classification, publication-wait scheduling, global pause behavior, handlers, and task lane assignments are unchanged. | Focused collection tests and diff review | Verified |
| AC6 | Formatting, Release build, all non-external tests, CodeGraph sync, record validation, diff/status, and secret checks pass. | Repository gates | Verified |
| AC7 | After deployment, Normal-lane completions advance while Realtime work remains queued; the affected horse attempt runs without `iv_h_name`, and `discovery:2026091306` reaches a successful terminal state without the Nakayama error. | Deployment and production job screens | Not started |

## Task plan

| ID | Task | Owner | Depends on | Write scope | Verification | State |
| --- | --- | --- | --- | --- | --- | --- |
| T1 | Confirm production starvation and scheduling call path. | Main | - | Read-only and this record | Production UI and CodeGraph | Verified |
| T2 | Implement bounded non-Realtime fairness with deterministic mixed-lane ordering. | Main | Approval | Allocator only | Unit tests | Verified |
| T3 | Add dispatcher-level mixed-lane regression coverage. | Main | T2 | API tests | Focused integration tests | Verified |
| T4 | Run repository gates, self-review, update docs, commit, and push. | Main | T2-T3 | Tests/docs/derived graph | AC6 | In progress |
| T5 | Deploy and complete the two pending production recovery observations. | Main | T4 | Approved production recovery operations | AC7 | Dependent |

## Review gates

- **Design and task-split review — 2026-09-17, reviewer: Main.** CodeGraph and production evidence confirm
  that the blockage is in the shared allocator, not the deployed horse-field readiness logic. The proposed
  change is confined to selection of an already-due envelope. It neither broadens retries nor weakens safety
  stops. Implementation remains serialized because allocator semantics, dispatcher persistence, deployment,
  and production recovery form one shared-state path.
- **Pre-implementation review — 2026-09-17, reviewer: Main.** The user explicitly approved AC1-AC7 after
  confirming the no-Realtime 1:1 behavior. The weighted rotation, work-conserving empty-lane behavior,
  unchanged within-lane ordering, and unchanged safety/retry semantics are frozen. T2 is `In progress`;
  T3-T5 are `Dependent`.
- **Checkpoint review — 2026-09-17, reviewer: Main.** The implementation persists the consecutive-Realtime
  count and last non-Realtime lane from dispatched envelopes, so restart does not reset the rotation. The
  allocator is work-conserving and changes only lane choice; existing within-lane effective priority and
  stable tie breakers remain. Focused allocator tests passed 12/12, dispatcher tests passed 9/9, Release build
  passed with zero warnings/errors, and all 1,061 non-external tests passed with one existing skip. No retry,
  handler, pause, or task-assignment code changed. T2-T3 are `Verified`; T4 is `In progress`.
- **Final review:** pending.

## Verification record

- 2026-09-17: production changed from 1,586 to 1,584 waiting and from 1,348 to 1,351 recently completed,
  confirming that execution is active. The two Normal recovery resources did not receive new attempts.
- 2026-09-17: sampled Realtime jockey work returned `公開待ち` in attempts 17-21 at intervals while remaining
  eligible, confirming a persistent higher-ranked lane rather than a stopped pipeline.
- 2026-09-17: source trace confirmed `CollectionLaneAllocator.Select` orders Realtime before Normal and only
  forces Background after the consecutive-Realtime threshold.
- 2026-09-17: the user approved the 80%/10%/10% all-lanes rotation, 1:1 Normal/Background behavior when
  Realtime is empty, and work-conserving transfer of empty-lane capacity.
- 2026-09-17: deterministic allocator coverage passed for Realtime preference, Background anti-starvation,
  80%/10%/10% rotation, and no-Realtime 1:1 alternation. Dispatcher integration coverage passed across a
  dispatcher reconstruction, proving persisted rotation state.
- 2026-09-17: formatting verification and Release build passed with zero warnings/errors. All non-external
  tests passed: 1,061 passed and one existing skip.
