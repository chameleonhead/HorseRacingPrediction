# Collection application and runtime efficiency

- Status: Proposed
- Owner: HorseRacingPrediction team
- Created: 2026-09-15
- Updated: 2026-09-15

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | This lower-priority change requires separate approval. |
| Verification | Not started | API/EventStore/runtime baselines and fault tests remain. |
| Deployment/operation | Not started | No AWS configuration has changed. |

## Context

This record isolates application, persistence, transport and Lambda tuning from [Snapshot-first collection and bulk ingestion](../20260915_snapshot-first-bulk-ingestion/README.md). The current result-bulk endpoint reduces HTTP calls but still publishes Race/entry commands sequentially and can perform up to 54 Horse/Jockey/Trainer existence queries for 18 distinct entries. Several same-value updates emit new events. Runtime evidence shows cold starts are only 1.20% of recent invocations, so image slimming is not the primary work.

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
| AC1 | For 18 entries, all items are validated before mutation; rejected items get structured outcomes and the valid Race event set loads/commits once per stage. Commit failure checkpoints none; post-commit loss replays as no-op. Subject existence uses at most three set queries per envelope. | A1 | Aggregate/query/event/projection instrumentation and pre/during/post-commit fault tests. | Not started |
| AC2 | Reapplying the same normalized Race/Horse/Jockey/Trainer values under another capture appends zero state-change events and performs zero state projection updates; historical citations are separately defined. Idempotent upsert removes Collector GET-before-write and survives concurrent replay. | A2 | Two-capture/concurrent integration tests through real persistence. | Not started |
| AC3 | Telemetry attributes at least 95% of successful wall time without secrets. Memory tuning uses at least 30 runs per task kind/config, selects minimum GB-seconds with p95 regression at most 5%, zero timeouts and peak memory below 85%; `/tmp` is unchanged until high-water evidence exists. | A3 | Coverage test, power report, `/tmp` report and config diff. | Not started |
| AC6 | Existing tests, formatting and Release build pass; HTTP calls and total write transactions fall at least 80% on the fixed 12-race × 18-entry comparison without changing persisted outputs or failure semantics. | A1–A3 | CI-equivalent suite and instrumented baseline/candidate report. | Not started |

## Delivery plan

1. Keep this change lower priority than the Playwright efficiency work.
2. A1 freezes persistence counters and implements aggregate/set-query grouping in one checkpoint.
3. A2 adds semantic no-op/upsert independently.
4. A3 measures runtime; it does not change memory or `/tmp` unless gates pass.
5. Observe one collection window per enabled slice; cleanup is separate.

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| A1 | Instrument and implement aggregate/set-query grouping. Covers AC1, AC6. | Main | Lead tier | - | API/Application/persistence and tests | Fault and count tests | Bounded grouped application | Proposed |
| A2 | Implement semantic no-op and idempotent subject upsert. Covers AC2, AC6. | Main | Lead tier | A1 | Domain/Application and tests | Replay/concurrency tests | Zero same-state growth | Proposed |
| A3 | Add runtime telemetry and bounded tuning reports. Covers AC3, AC6. | Worker | Worker tier | A1 | Metrics/IaC reports; configuration only after gate | Coverage/power/config checks | Lowest measured GB-seconds or no change | Proposed |

Shared API/domain files remain under one owner and A2 follows A1. Transport compression and retry are excluded so this record can complete independently of Snapshot-first.

## Review gates

- **Design and task-split review** — 2026-09-15, independent R10/R12 plus 2026-09-15 lead reconciliation after Playwright completion. R10 required exclusive persistence ownership and a concrete dependency/state for transport work. To keep this change independently completable, transport compression/retry (former A4/AC4/AC5) is now an explicit non-goal pending the Snapshot-first contract. A1–A3 have serialized ownership where files overlap, observable verification, and no browser/pilot dependency. The prior R12 isolation finding remains satisfied.
- **Pre-implementation review** — Pending explicit approval.
- **Checkpoint review** — Pending implementation.
- **Final review** — Pending full evidence reconciliation.

## Documentation updates

- `docs/01-lambda-collector-architecture.md`: links this separate runtime/application optimization scope.
- Snapshot-first and Playwright records remain separate authorities for their concerns.

## Verification record

- 2026-09-15: Read-only code/AWS audit established current sequential command/query behavior and 68 cold starts among 5,672 reports; no runtime change was made.
- No production source, database or AWS resource was changed.

## Deviations and follow-up

- Container image slimming and EventStore replacement remain separate future decisions.
- Compression and deadline-aware HTTP retry remain part of a future transport record after the idempotent Snapshot-first contract is approved; they are not authorized by approval of this record.
