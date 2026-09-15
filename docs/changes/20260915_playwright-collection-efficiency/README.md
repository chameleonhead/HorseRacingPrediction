# Playwright collection efficiency

- Status: Proposed
- Owner: HorseRacingPrediction team
- Created: 2026-09-15
- Updated: 2026-09-15

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | Explicit approval is required before source changes. |
| Verification | Not started | Baseline, equivalence, component and bounded-live measurements remain. |
| Deployment/operation | Not started | No production configuration is changed by the design. |

## Context

This is the higher-priority, standalone optimization extracted from [Snapshot-first collection and bulk ingestion](../20260915_snapshot-first-bulk-ingestion/README.md). It must establish its own baseline and savings so later Snapshot-first measurements do not claim Playwright/parser improvements.

Current typed browser operations can repeat readiness and extract page text that JRA callers discard. Link discovery and re-resolution enumerate anchors with per-element Playwright calls: static bounds for 100 anchors are roughly 701–1,301 repository-level calls for extraction plus about 304 for click re-resolution. One result capture may rebuild `JraSnapshotView` up to six times. Horse fallback discovery can validate a profile and then rebuild the same search to capture it again.

## Goals

- Measure browser, Snapshot, navigation and parser phases before behavior changes.
- Remove redundant waits and unused text extraction from typed JRA paths.
- Make candidate discovery constant in repository-level Playwright calls without wrong-element actions.
- Share one immutable `JraSnapshotView` and typed parse per capture.
- Reuse bounded validated Horse results and unchanged current-page captures.
- Evaluate purpose-specific Snapshot projection, safe resource interception and one sequential transient child page behind strict gates.

## Non-goals

- Snapshot-first ingestion contracts, API inbox/outbox, EventStore grouping or subject-request bulk writes.
- Concurrent page navigation or production parallel tabs.
- Persisting raw browser JSON/full semantic DOM or cross-invocation browser profiles.
- Guessing direct URLs for JavaScript/POST/session-bound JRA actions.
- Enabling resource blocking or lossy Snapshot projection without equality and performance evidence.

## Design

### Measurement-first slices

P1 adds counters for ready barriers/timeouts, legacy text reads, semantic captures, `JraSnapshotView` projections, typed parses, navigation actions, repository-level Playwright calls, transferred bytes, elapsed time and allocations. The initial baseline is committed before optimizations.

P2 changes typed JRA operations to one page-kind-specific post-action readiness barrier followed by no-wait semantic capture. Existing text-returning APIs remain for non-JRA callers.

P3 replaces per-element candidate discovery with one browser-side evaluation producing descriptors and a DOM-mutation generation. After C# scoring, one revalidation requires an unchanged generation and unique complete actionable fingerprint, returns the exact `ElementHandle`, and only that handle is operated. Mutation, ambiguity, replacement or detach permits one re-resolution and then safe failure.

P4 constructs one immutable `JraSnapshotView` per capture for identification and parsing. Subject parsing returns one normalized result shared by profile, identity, pedigree and history consumers.

P5 retains only normalized Horse candidate results and bounded identity evidence—maximum 32 candidates and 256 KiB. It removes the second search reconstruction and duplicate race-card current-page capture. Page/link caches are session-local and invalidated by navigation, same-page action or DOM mutation.

P6 inventories requests/bytes and evaluates resource blocking and page-purpose Snapshot fields. These candidates remain disabled unless their acceptance gates pass. P7 may introduce one sequential same-context child page; parent and child have page-local browser/reader/navigator state and cleanup in `finally`. Parallel children remain unconditionally disabled.

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | The baseline records at least 95% of typed JRA operation wall time across readiness, navigation/action, text extraction, Snapshot evaluation/transfer/deserialization, view projection and parsing, without secrets. | P1 | Instrumentation coverage tests and fixed baseline report. | Not started |
| AC2 | Each typed JRA action has at most one post-action readiness barrier, zero discarded legacy full-page text reads, and one semantic capture per parsed terminal page; delayed required data is awaited while delayed image/font/tracker is not. | P2 | Delayed-data/resource fixtures and exact normalized-output/failure comparison. | Not started |
| AC3 | Candidate discovery uses at most one batch evaluation, one mutation-safe unique revalidation and one `ElementHandle` action independent of 10/100/500 candidates. Mutation/reorder/replacement/duplicate/detach never clicks another element; 100-link wall time improves at least 80%. | P3 | Five warm iterations per size plus first/middle/last and adversarial DOM mutation tests. | Not started |
| AC4 | Every captured JRA page creates at most one shared `JraSnapshotView` and one selected typed parse; parser order, fields, citations and diagnostics equal baseline. | P4 | Instrumented parser suite and 70-history/result fixtures with allocation/time report. | Not started |
| AC5 | Horse fallback opens search once, parses each candidate once, retains at most 32 candidates/256 KiB, and returns the unique retained result without replaying top/search/page/profile. Overflow is structured ambiguity. Race-card fallback snapshots an unchanged page once. | P5 | 32/33 and byte-boundary navigation-trace fixtures with exact result/source evidence. | Not started |
| AC6 | Resource blocking remains off unless 10 warm fixture iterations per page kind/candidate and three bounded-live repetitions show exact normalized output/diagnostic equality, zero added identity/parse/access-limit failures, and at least 15% lower browser-held p50 and p95. | P1, P6 | Request waterfall/bytes/equality/failure report and production-registration assertion. | Not started |
| AC7 | A purpose-specific Snapshot projection is adopted only when the same corpus preserves fields, citations, identity and diagnostics and improves capture-stage p95 at least 10%; otherwise full safe-pruning capture remains. | P1, P6 | Projection matrix with evaluation/JSON/deserialization/allocation measurements. | Not started |
| AC8 | One sequential child page shares context but has page-local state; all success/failure/cancellation paths close it and preserve the parent. Production maximum concurrent navigation is one, and benchmark results cannot authorize parallel rollout. | P7 | Lifecycle/cookie/isolation/cleanup tests and production registration assertion. | Not started |
| AC9 | Existing non-external tests, formatting, Release build and bounded JRA regression paths pass after each enabled slice; rejected candidates remain disabled and documented with evidence. | P1–P7 | CI-equivalent checks, bounded-live report and configuration diff. | Not started |

## Delivery plan

1. Approve this record independently; Snapshot-first approval is neither required nor implied.
2. P1: add measurement seams and freeze the baseline without changing behavior.
3. P4: share `JraSnapshotView`/typed results; verify and commit independently.
4. P2: remove redundant readiness/text work; verify and commit independently.
5. P3: batch candidate discovery with mutation-safe actions; verify and commit independently.
6. P5: remove search/navigation recapture; verify and commit independently.
7. P6: evaluate resource/Snapshot candidates; enable only passing candidates.
8. P7: evaluate one sequential child page last because it changes page ownership.
9. Compare the final result with the frozen baseline and observe one collection window before cleanup.

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| P1 | Instrument and freeze browser/Snapshot/navigation/parser baseline. Covers AC1, AC6, AC7, AC9. | Main | Lead tier | - | Scraping metrics/test seams and benchmark artifacts | Coverage tests and baseline | Reviewed reproducible baseline | Proposed |
| P2 | Implement typed no-text navigation and one page-specific readiness barrier. Covers AC2, AC9. | Main | Lead tier | P1,P4 | Browser/JRA navigation and focused tests | Delayed fixtures and regression corpus | Equivalent outputs with lower waits | Proposed |
| P3 | Implement batch candidate discovery and mutation-safe handle action. Covers AC3, AC9. | Main | Lead tier | P1,P2 | Browser candidate/action layer and tests | RPC bounds, adversarial DOM and benchmark | Constant repository-call discovery | Proposed |
| P4 | Share one view and one typed parse per capture. Covers AC4, AC9. | Main | Lead tier | P1 | JRA parsing/reader and tests | Parser suite, allocation/time | One projection/parse with equality | Proposed |
| P5 | Reuse bounded Horse results/current-page captures and generation caches. Covers AC5, AC9. | Main | Lead tier | P3,P4 | JRA navigation and focused tests | Navigation trace/boundary tests | No search reconstruction | Proposed |
| P6 | Evaluate resource interception and page-purpose Snapshot projections. Covers AC6, AC7, AC9. | Worker | Worker tier | P1–P5 | Browser policy/options, benchmarks and tests | Required fixture/live matrices | Passing candidates enabled or measured rejection | Proposed |
| P7 | Add one sequential page-scoped child lease where measured useful. Covers AC8, AC9. | Main | Lead tier | P3,P5 | Browser/session abstraction and tests | Lifecycle/isolation/config tests | One-page policy with zero leaks | Proposed |

Shared browser/parser files are serialized in the order above. P6 may gather read-only baseline evidence earlier but does not write until P1–P5 are frozen. No task may enable concurrent navigation.

## Review gates

- **Design and task-split review** — 2026-09-15, independent R10/R12. R10 found cross-record scope/dependency issues in the initial extraction. After removing all Snapshot/API/runtime ownership and freezing this record's complete AC1–AC9 result as the pilot prerequisite, R12 returned `PASS`. Task write scopes are serialized; resource/projection candidates remain measurement-gated and parallel navigation remains excluded.
- **Pre-implementation review** — Pending explicit approval.
- **Checkpoint review** — Pending implementation.
- **Final review** — Pending complete evidence reconciliation.

## Documentation updates

- `docs/23-jra-scraping-redesign.md`: remains the canonical component architecture and links this change for optimization gates.
- `docs/01-lambda-collector-architecture.md`: remains the runtime architecture and distinguishes this work from Snapshot-first.

## Verification record

- 2026-09-15: CodeGraph and targeted source inspection identified repeated readiness/text work, per-element Playwright calls, repeated view projection and search reconstruction.
- 2026-09-15: Static estimates and existing fixtures were used only to design measurement gates; no production speedup is claimed yet.
- No production code, AWS resource or external data was changed.

## Deviations and follow-up

- Snapshot-first and application/runtime improvements are separate records and require separate approval.
- Parallel tabs remain a future separately approved change even if diagnostic results are favorable.
