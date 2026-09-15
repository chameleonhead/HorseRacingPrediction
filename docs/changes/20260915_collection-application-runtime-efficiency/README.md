# Collection application and runtime efficiency

- Status: Approved
- Owner: HorseRacingPrediction team
- Created: 2026-09-15
- Updated: 2026-09-16

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Checkpoints implemented | Race grouping, set-prefetch, semantic no-op, PUT upsert, and bounded runtime telemetry are committed. |
| Verification | In progress | AC2–AC3 pass. AC1 needs its remaining injected commit/query counters. AC6 build/format/output checks pass, but the approved 80% HTTP target is not attainable from the measured pre-change baseline; see `application-baseline.md`. |
| Deployment/operation | No change justified | AWS memory and `/tmp` remain unchanged because task-kind/config cohorts do not satisfy the tuning gate. |

## Context

This record isolates application, persistence, transport and Lambda tuning from [Snapshot-first collection and bulk ingestion](../20260915_snapshot-first-bulk-ingestion/README.md). The current result-bulk endpoint reduces HTTP calls but still publishes Race/entry commands sequentially and can perform up to 54 Horse/Jockey/Trainer existence queries for 18 distinct entries. Several same-value updates emit new events. Runtime evidence shows cold starts are only 1.44% of the rolling 14-day cohort, so image slimming is not the primary work.

## Goals

- Measure aggregate loads, transactions, events, projection writes, payload bytes, retries and runtime phases.
- Group valid Race event application and prefetch referenced subjects as sets.
- Make same-state collection a semantic no-op and replace GET-before-write with idempotent upsert.
- Gate Lambda/runtime settings on measured total cost.

## Non-goals

- Playwright/Snapshot/navigation changes or Snapshot-first ownership handoff itself.
- New AWS services, higher concurrency, EventStore replacement or whole-race distributed atomicity.
- Transport compression/retry changes, which depend on the separately proposed Snapshot-first ingestion contract.
- Changing memory/storage without the specified gate.

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | For 18 entries, all items are validated before mutation; rejected items get structured outcomes and the valid Race event set loads/commits once per stage. Commit failure checkpoints none; post-commit loss replays as no-op. Subject existence uses at most three set queries per envelope. | A1 | Aggregate/query/event/projection instrumentation and pre/during/post-commit fault tests. | Connected |
| AC2 | Reapplying the same normalized Race/Horse/Jockey/Trainer values under another capture appends zero state-change events and performs zero state projection updates; historical citations are separately defined. Idempotent upsert removes Collector GET-before-write and survives concurrent replay. | A2 | Two-capture/concurrent integration tests through real persistence. | Verified |
| AC3 | Telemetry attributes at least 95% of successful wall time without secrets. Memory tuning uses at least 30 runs per task kind/config, selects minimum GB-seconds with p95 regression at most 5%, zero timeouts and peak memory below 85%; `/tmp` is unchanged until high-water evidence exists. | A3 | Coverage test, power report, `/tmp` report and config diff. | Verified |
| AC6 | Existing tests, formatting and Release build pass; HTTP calls and total write transactions fall at least 80% on the fixed 12-race × 18-entry comparison without changing persisted outputs or failure semantics. | A1–A3 | CI-equivalent suite and instrumented baseline/candidate report. | Connected |

## Delivery plan

1. Keep this change lower priority than the Playwright efficiency work.
2. A1 freezes persistence counters and implements aggregate/set-query grouping in one checkpoint.
3. A2 adds semantic no-op/upsert independently.
4. A3 measures runtime; it does not change memory or `/tmp` unless gates pass.
5. Observe one collection window per enabled slice; cleanup is separate.

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| A1 | Instrument and implement aggregate/set-query grouping. Covers AC1, AC6. | Main | Lead tier | - | API/Application/persistence and tests | Fault and count tests | One Race command, at most three subject set queries, replay adds zero events | In progress |
| A2 | Implement semantic no-op and idempotent subject upsert. Covers AC2, AC6. | Main | Lead tier | A1 | Domain/Application and tests | Replay/concurrency tests | Equal values add zero events; concurrent PUT upsert succeeds; Collector uses no normal upsert GET | Verified |
| A3 | Add runtime telemetry and bounded tuning reports. Covers AC3, AC6. | Worker | Worker tier | A1 contract; read-only discovery may start now | Metrics/IaC reports; configuration only after gate | Coverage/power/config checks | Safe ≥95% phase attribution; runtime config retained by gate | Verified |

Shared API/domain files remain under one owner and A2 follows A1. Transport compression and retry are excluded so this record can complete independently of Snapshot-first.

## Review gates

- **Design and task-split review** — 2026-09-15, independent R10/R12 plus 2026-09-15 lead reconciliation after Playwright completion. R10 required exclusive persistence ownership and a concrete dependency/state for transport work. To keep this change independently completable, transport compression/retry (former A4/AC4/AC5) is now an explicit non-goal pending the Snapshot-first contract. A1–A3 have serialized ownership where files overlap, observable verification, and no browser/pilot dependency. The prior R12 isolation finding remains satisfied.
- **Pre-implementation review** — 2026-09-15, reviewer: Main. User explicitly approved AC1/AC2/AC3/AC6 and requested continuous execution. A1 is `Runnable`; A2 is serialized behind A1 because aggregate/domain/persistence semantics overlap. A3 production changes remain `Dependent` on the A1 measurement contract, while read-only runtime/IaC inventory may proceed with a disjoint scope. Worker inputs are the approved record, current CodeGraph/source, existing tests and read-only AWS evidence; required evidence is exact call paths, counters/config surfaces and reproducible commands. Escalate on public-contract changes, data migration, destructive AWS operations, persistence incompatibility, or acceptance-criterion conflict. Compression/retry, Snapshot-first, browser changes and concurrent collection remain out of scope.
- **Checkpoint review** — 2026-09-15, reviewer: Main. The legacy sequential Race-result branch was removed after focused Domain/Application/API tests passed. Review found and corrected loss of popularity, original finish position, dead-heat, average-1F, HorseId/JockeyId citation, and additional prize fields in the grouped event. Structured outcomes now distinguish validation, related-subject, and Race-command failures. CodeGraph confirms one `ApplyBulkRaceResultCommand` dispatch and the source contains one set query per subject type.
- **Final review** — 2026-09-16, independent reviewer: `/root/final_review`, result: Revise. The review found subject mutation before Race validation; the order was corrected with pure prevalidation and a real EventStore test proving invalid Race state adds zero Race or subject events. It also required real-persistence concurrent replay evidence for all three subject types; those tests now assert one aggregate event after concurrent and sequential identical PUTs. Remaining findings are AC1's injected during-commit/query instrumentation and AC6's measured HTTP shortfall. Release build, format, diff checks, real EventStore replay, concurrent upsert, and runtime telemetry tests pass. The external JRA E2E suite had three calendar-data failures because July 2026 returned no meeting dates; all 213 API tests and all other completed test projects passed.

## Documentation updates

- `docs/01-lambda-collector-architecture.md`: links this separate runtime/application optimization scope.
- Snapshot-first and Playwright records remain separate authorities for their concerns.

## Verification record

- 2026-09-15: Read-only code/AWS audit established current sequential command/query behavior and 83 cold starts among 5,750 rolling 14-day reports; no runtime configuration change was made.
- 2026-09-15: Release build passed with zero warnings. Domain 105, Contracts 43, Application 57, Infrastructure 13, MachineLearning 14, Agents 107, Collector 201, and API 213 tests passed; API had one intentional skip. Scraping completed 253 tests with five skips and three external-calendar failures unrelated to the changed paths.
- 2026-09-15: `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`, `git diff --check`, and CodeGraph synchronization passed.
- No database migration or AWS resource was changed.

## Deviations and follow-up

- Container image slimming and EventStore replacement remain separate future decisions.
- Compression and deadline-aware HTTP retry remain part of a future transport record after the idempotent Snapshot-first contract is approved; they are not authorized by approval of this record.
- The pre-change collector already sent one result-bulk HTTP request per race. The approved 80% HTTP reduction cannot be obtained by removing one existence GET from each single-subject upsert (measured reduction: 50%). Achieving it requires approval of a multi-subject transport/buffering contract; this blocks AC6 completion but does not invalidate the verified Race persistence, replay, or runtime changes.
