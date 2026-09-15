# Playwright collection efficiency

- Status: Implemented
- Owner: HorseRacingPrediction team
- Created: 2026-09-15
- Updated: 2026-09-15

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Complete | P1–P5 are implemented; P6/P7 candidates were evaluated and correctly left disabled. |
| Verification | Complete | Component, full Release, formatting and bounded-live checks pass. |
| Deployment/operation | Complete | No AWS/runtime change; production remains one sequential page with full safe-pruning Snapshot capture. |

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
| AC1 | The baseline records at least 95% of typed JRA operation wall time across readiness, navigation/action, text extraction, Snapshot evaluation/transfer/deserialization, view projection and parsing, without secrets. | P1 | Instrumentation coverage tests and fixed baseline report. | Verified |
| AC2 | Each typed JRA action has at most one post-action readiness barrier, zero discarded legacy full-page text reads, and one semantic capture per parsed terminal page; delayed required data is awaited while delayed image/font/tracker is not. | P2 | Delayed-data/resource fixtures and exact normalized-output/failure comparison. | Verified |
| AC3 | Candidate discovery uses at most one batch evaluation, one mutation-safe unique revalidation and one `ElementHandle` action independent of 10/100/500 candidates. Mutation/reorder/replacement/duplicate/detach never clicks another element; 100-link wall time improves at least 80%. | P3 | Five warm iterations per size plus first/middle/last and adversarial DOM mutation tests. | Verified |
| AC4 | Every captured JRA page creates at most one shared `JraSnapshotView` and one selected typed parse; parser order, fields, citations and diagnostics equal baseline. | P4 | Instrumented parser suite and 70-history/result fixtures with allocation/time report. | Verified |
| AC5 | Horse fallback opens search once, parses each candidate once, retains at most 32 candidates/256 KiB, and returns the unique retained result without replaying top/search/page/profile. Overflow is structured ambiguity. Race-card fallback snapshots an unchanged page once. | P5 | 32/33 and byte-boundary navigation-trace fixtures with exact result/source evidence. | Verified |
| AC6 | Resource blocking remains off unless 10 warm fixture iterations per page kind/candidate and three bounded-live repetitions show exact normalized output/diagnostic equality, zero added identity/parse/access-limit failures, and at least 15% lower browser-held p50 and p95. | P1, P6 | Request waterfall/bytes/equality/failure report and production-registration assertion. | Verified |
| AC7 | A purpose-specific Snapshot projection is adopted only when the same corpus preserves fields, citations, identity and diagnostics and improves capture-stage p95 at least 10%; otherwise full safe-pruning capture remains. | P1, P6 | Projection matrix with evaluation/JSON/deserialization/allocation measurements. | Verified |
| AC8 | One sequential child page shares context but has page-local state; all success/failure/cancellation paths close it and preserve the parent. Production maximum concurrent navigation is one, and benchmark results cannot authorize parallel rollout. | P7 | Lifecycle/cookie/isolation/cleanup tests and production registration assertion. | Verified |
| AC9 | Existing non-external tests, formatting, Release build and bounded JRA regression paths pass after each enabled slice; rejected candidates remain disabled and documented with evidence. | P1–P7 | CI-equivalent checks, bounded-live report and configuration diff. | Verified |

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
| P1 | Instrument and freeze browser/Snapshot/navigation/parser baseline. Covers AC1, AC6, AC7, AC9. | Worker A | Worker tier | - | Scraping metrics/test seams and benchmark artifacts | Coverage tests and baseline | Reviewed reproducible baseline | Verified |
| P2 | Implement typed no-text navigation and one page-specific readiness barrier. Covers AC2, AC9. | Main | Lead tier | P1,P4 | Browser/JRA navigation and focused tests | Delayed fixtures and regression corpus | Equivalent outputs with lower waits | Verified |
| P3 | Implement batch candidate discovery and mutation-safe handle action. Covers AC3, AC9. | Main | Lead tier | P1,P2 | Browser candidate/action layer and tests | RPC bounds, adversarial DOM and benchmark | Constant repository-call discovery | Verified |
| P4 | Share one view and one typed parse per capture. Covers AC4, AC9. | Worker B | Worker tier | P1 measurement contract only | JRA parsing/reader and tests | Parser suite, allocation/time | One projection/parse with equality | Verified |
| P5 | Reuse bounded Horse results/current-page captures and generation caches. Covers AC5, AC9. | Main | Lead tier | P3,P4 | JRA navigation and focused tests | Navigation trace/boundary tests | No search reconstruction | Verified |
| P6 | Evaluate resource interception and page-purpose Snapshot projections. Covers AC6, AC7, AC9. | Main | Lead tier | P1–P5 | Browser policy/options, benchmarks and tests | Required fixture/live matrices | Candidates remain disabled because gates were not established | Verified |
| P7 | Evaluate one sequential page-scoped child lease where measured useful. Covers AC8, AC9. | Main | Lead tier | P3,P5 | Browser/session abstraction and tests | Lifecycle/isolation/config tests | Existing single page retained; no parallel/child rollout | Verified |

Shared browser/parser files are serialized in the order above. P6 may gather read-only baseline evidence earlier but does not write until P1–P5 are frozen. No task may enable concurrent navigation.

## Review gates

- **Design and task-split review** — 2026-09-15, independent R10/R12. R10 found cross-record scope/dependency issues in the initial extraction. After removing all Snapshot/API/runtime ownership and freezing this record's complete AC1–AC9 result as the pilot prerequisite, R12 returned `PASS`. Task write scopes are serialized; resource/projection candidates remain measurement-gated and parallel navigation remains excluded.
- **Pre-implementation review** — 2026-09-15, reviewer: Main. User explicitly approved Playwright AC1–AC9. P1 and P4 are `In progress` with disjoint initial write scopes: P1 owns measurement/test seams and benchmark artifacts; P4 owns JRA view/parser/reader code and its focused tests. P2/P3/P5/P6/P7 remain `Proposed` until their recorded dependencies are verified. Escalate on output-contract changes, shared-file overlap, live-site ambiguity, test regression after one focused correction, or any need for concurrent navigation/external mutation. Snapshot-first, application/runtime and subject records remain `Proposed` and out of scope.
- **Checkpoint review** — 2026-09-15, reviewer: Main. P1 records all seven required local pipeline phases; measured phase means account for 99.5% of the enclosing local fixture mean and contain no page data, credentials or external calls. The source-backed repository-call formulas are explicitly estimates and are not used as wire-trace claims. P4 uses a weak-key, execution-and-publication cache, preserving parser order and isolating distinct captures. Focused Release tests passed 14/14 and exact solution formatting passed. The subsequent full parser/regression suite closed P4; shared navigation work retained the independently committed historical-navigation behavior.
- **Checkpoint review (P2–P7)** — 2026-09-15, reviewer: Main. JRA callers use no-text actions and a no-wait capture after one semantic readiness barrier. Calendar readiness waits for its required year/month and table data while a slow image fixture completes without waiting for image load. Candidate extraction is one browser evaluation; actions use a uniquely ranked descriptor, exact handle revalidation and `ElementHandle` click with one retry. 10/100/500 extraction, duplicate safety and a five-iteration 100-link benchmark pass; p50 changed from 797.184 ms to 11.233 ms (98.6%). Horse search trace opens/submits once, does not replay the top/search/profile path, and 32/33, duplicate and exact 256 KiB boundaries pass. Resource blocking and lossy projections remain disabled because their fixture/live adoption gates were not established. A child page is likewise not introduced: after the browser work fell by 98.6%, another page would add browser cost without measured benefit; production remains one context, one page and one sequential navigation stream.
- **Final review** — 2026-09-15, reviewer: Main. All AC1–AC9 and P1–P7 trace to committed implementation, passing tests or an explicit disabled-candidate gate. Source compatibility was checked against the live horse search and old race-result paths; image-alt-only race links were restored before acceptance. Resource blocking, lossy projections, child pages and concurrent navigation have no production registration. Exact formatting, zero-warning Release build, 974 passing non-external tests with one intentional skip, CodeGraph synchronization and change-record validation complete the gate. Worker P1/P4 output was independently reviewed and accepted without rework; no systemic orchestration failure was found.

## Documentation updates

- `docs/23-jra-scraping-redesign.md`: remains the canonical component architecture and links this change for optimization gates.
- `docs/01-lambda-collector-architecture.md`: remains the runtime architecture and distinguishes this work from Snapshot-first.

## Verification record

- 2026-09-15: CodeGraph and targeted source inspection identified repeated readiness/text work, per-element Playwright calls, repeated view projection and search reconstruction.
- 2026-09-15: Static estimates and existing fixtures were used only to design measurement gates; no production speedup is claimed yet.
- 2026-09-15: P1 added a repeatable local fixture probe and fixed source-backed operation-count scenarios. The initial one-warm/five-measured run covered navigation, readiness, discarded text, Snapshot capture, view, parser, repository-call seam and the enclosing total; phase means were 99.5% of the enclosing mean.
- 2026-09-15: P4 made `JraSnapshotView` reuse one immutable projection per captured `PageSnapshot` reference through a weak-key cache. Same-reference, distinct-reference and concurrent-access tests pass; selected parsing remains once in `JraPageReader`.
- 2026-09-15: Lead verification passed the focused P1/P4 Release tests (14/14) and `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`.
- 2026-09-15: Browser efficiency component tests passed 6/6. The 100-link five-iteration p50 comparison measured 797.184 ms for the legacy locator loop and 11.233 ms for batch extraction (98.6% lower); 10/100/500 candidate counts and duplicate safe-failure passed.
- 2026-09-15: Horse bounded-evidence and no-replay tests passed 4/4; the live `HorseProfileSearch_DaiyuVenti` path passed 1/1.
- 2026-09-15: The bounded live `古いRaceResult取得` path passed 1/1 after restoring image-alt link-title equivalence. A dynamic two-month calendar discovery test found no meetings for its computed month and is recorded as live-data-dependent, not used as acceptance evidence.
- 2026-09-15: Resource blocking, purpose-specific lossy projection, child pages and concurrent navigation were not enabled. Their rollout gates were not met, so the cheaper single-page/full safe-pruning configuration remains authoritative.
- 2026-09-15: `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes` passed.
- 2026-09-15: `dotnet build HorseRacingPrediction.sln --no-restore --configuration Release` passed with zero warnings and zero errors.
- 2026-09-15: `dotnet test HorseRacingPrediction.sln --no-build --configuration Release --filter "TestCategory!=External"` passed 974 tests with one intentional skip.
- 2026-09-15: Worker routing — P1 and P4 were accepted on first lead review; rework and escalation count zero. Provider token/cost telemetry was unavailable, so no numeric cost claim is made.
- No AWS resource or external data was changed.

## Deviations and follow-up

- Snapshot-first and application/runtime improvements are separate records and require separate approval.
- Parallel tabs remain a future separately approved change even if diagnostic results are favorable.
