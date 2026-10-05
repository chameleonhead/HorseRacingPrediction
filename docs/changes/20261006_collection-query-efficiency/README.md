# Preserve collection behavior while reducing repeated database work

- Status: Approved
- Change record schema: 2
- Owner: Collection operations
- Created: 2026-10-06
- Updated: 2026-10-06
- JRA site contract impact: None

## Context and authorization

The user requests efficiency improvements and permits behavior-changing work only when benefit is expected and the change is appropriate. The preceding investigation identified two bounded inefficiencies: identical execution-lease count/list queries and scheduled-state filtering after unbounded materialization. This record replaces the unapproved five-second interval/index design in [the investigation record](../20261005_collection-cpu-load/README.md). The historical [loop inventory](../20261005_collection-cpu-load/evidence/service-loop-inventory.md) remains evidence at revision09566e95, not proof of implementation or live CPU attribution.

The implementation contract preserves the one-second dispatcher interval, all service registrations and clocks, prioritization, capacity, lease expiry, transactions, hold handling, recovery and duplicate prevention. No UI/API/domain contract change, service removal, hosting change, data repair, bulk retry or failure dismissal. No index is added unless actual provider evidence establishes a useful plan and its additive migration is separately recorded before implementation. Removing duplicate queries and bounding materialization are mechanical outcomes; total CPU reduction is not guaranteed without process profiling.

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | Await repository-informed first-write gate. |
| Verification | Not started | Real SQLite parity, query shape/count, existing regressions and exact CI gates. |
| Deployment/operation | Not started | Canonical deployment and bounded health/progress/metric comparison after verified code. |

## Concern and agreement ledger

| ID | Evidence, counterexample and disposition | AC/task | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- |
| C1 | Source confirms identical Count/list predicates in execution-lease reclaim. Return the materialized count while retaining every transaction and release effect. Zero, expired and unexpired leases require actual-store proof. | AC1/T2 | Favor query consolidation, not slower polling. | User requested efficiency with conditional behavior changes. | Resolved in design |
| C2 | Due-state query reads all non-Collecting states then filters/orders/limits in memory. No secondary ordering is specified, so an equal-time group crossing the limit has no guaranteed membership. Keep ascending due time without introducing a new secondary priority; tied membership may differ within the same eligible timestamp. This internal selection variation is appropriate under conditional user authorization because it bounds materialization while preserving the scheduling contract. Provider offset conversion, eligibility, non-tied results and all snapshot fields require actual SQLite proof. | AC2/T2,T3 | Accept unspecified tied order only; no priority/eligibility/time change. Reconsider if callers establish a contrary ordering contract. | User explicitly authorized appropriate, beneficial efficiency changes; bounded tie behavior communicated. | Resolved in design |
| C3 | Live process profiling/SSH unavailable in previous investigation. A useful synthetic index plan does not prove actual benefit. Keep existing indexes; omit additional indexes if provider evidence does not justify them. Preserve uncertainty in production observations. | AC3/T3,T4 | No blind index sweep or all-CPU-resolution claim. | Scope narrowed to evidence-backed improvements. | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Execution-lease reclaim avoids the redundant count query; returned count, expired statuses, release side effects and transactional/concurrency safety remain unchanged. | T2,T3 | SQLite command capture and existing reclaim/dispatch regression tests. | Not started |
| AC2 | Due-state selection filters and bounds database materialization without changing eligibility, date interpretation, intended ordering, limit clamp or snapshot values. | T2,T3 | Actual SQLite SQL plus old/new result parity for null/future/Collecting/equality/offset/limit/ties; scheduler regressions. | Not started |
| AC3 | Exact formatter/build/tests and matching Ubuntu workflow gates pass; canonical deployment is healthy and eligible work continues; compare bounded CPU/burst/latency observations without attributing all CPU to these queries. | T3,T4 | Mechanical gates, terminal workflow evidence and safe production reads. | Not started |
| AC4 | Dispatcher stays one second; service clocks, allocation/recovery safeguards, data and unrelated work remain unchanged; no secrets or destructive/bulk operations. | T1,T2,T3,T4 | Attributed diff, audit/DDD validators, existing safety regressions and operation ledger. | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Define frozen scope, resolve material concerns and first-write readiness. | Lead | Lead | User request | This record; docs/changes/20261005_collection-cpu-load/README.md; docs/26-collection-platform-design.md | Planner findings and canonical validators | Planner confirmed two-file slice, existing interceptors/index and no secondary ordering contract; canonical audit/DDD passed before release | Verified | Architecture, security and integration decisions | none | unavailable; retries 0; corrections 0; reviews 1 |
| T2 | Consolidate lease query and push down due selection with real-provider regression proof. | collection_resume_diagnosis worker | Luna high | T1 | src/HorseRacingPrediction.CollectionOperations/CollectionPlatform/CollectionPlatformStore.cs; tests/HorseRacingPrediction.Collector.Tests/CollectionPlatform/CollectionPlatformStoreTests.cs | Focused query/reclaim/scheduler tests and related API/Collector regression | [T2-A1](agent-audits/T2-A1.json); first-write release after passing audit | In progress | Worker — bounded serial implementation after frozen decisions | T2-A1 | unavailable; retries 0; corrections 0; reviews 0 |
| T3 | Verify frozen final source on matching Ubuntu workflow. | verify_query_efficiency worker | Luna high | T2 | read-only | Exact workflow restore/format/build/Chromium/EF/solution tests and safety scripts | [T3-A1](agent-audits/T3-A1.json); preparation only until frozen-source release | Dependent | Worker — mechanical verifier, no repository edits | T3-A1 | unavailable; retries 0; corrections 0; reviews 0 |
| T4 | Deploy canonical verified revision, observe live outcome and final acceptance. | Lead | Lead | T3 | This record; canonical deployment and guarded existing pipeline operation | Exact-SHA terminal workflow, health/progress and bounded metrics | No operation performed | Dependent | Security-sensitive production operations and final acceptance | none | unavailable; retries 0; corrections 0; reviews 0 |

## Documentation updates

- docs/26-collection-platform-design.md: retain one-second interval and link the behavior-preserving query-efficiency contract instead of the unapproved interval proposal.
- Previous CPU record: preserve historical investigation and mark the old design Superseded with this link.

## Review gates and next action

Lead loaded agent-task-orchestration, document-driven-development and production-incident-recovery plus their required references in this turn. Existing collection_resume_diagnosis requested gpt-6-luna/high route is used for read-only Cheap Planner; observed model and usage are unavailable. The active worker is not authorized to edit implementation yet. Unrelated AGENTS/orchestration/routing changes are preserved. Sequential ownership avoids overlapping store/test edits.

Design/task-split review: Lead accepts the user-authorized bounded two-query outcome, not the former five-second proposal. C2 is resolved by retaining the existing unspecified tied-order contract, with tied eligibility/cardinality and strict non-tied parity proof. No new index/migration is planned; current state index is Status,NextCollectionAt. No material open decision remains. T2 owns exactly two files, T3 a read-only snapshot, T4 production security/final acceptance; no parallel implementation writes.

Pre-implementation review: purpose is duplicate-query removal and bounded scheduled-state reading. CodeGraph planner traced public/private execution-lease helpers and scheduler caller; actual SQLite interceptor and internal options constructor already exist. First remove Count and return private list count, retaining immediate transaction and all releases. Then push due predicate/order/Take(Math.Max(1,limit)) into SQL and project identical snapshots. New tests in the owned test file must cover empty/expired/unexpired/terminal/repeated reclaim and SQL SELECT count, and due null/Collecting/future/equality/offset, limit1/nonpositive/default500/>500, equal-time boundary. Compare old materialized algorithm exactly for non-ties; ties assert membership eligibility, count, ascending timestamps and no new priority. Capture generated SQL/EXPLAIN using the actual provider; omit new indexes if no verified need. No abstractions, service/config edits or external operations by worker. Unexpected public-contract/transaction/date semantics return to Lead before that edit.

Verification commands: focused `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj -c Release --filter "FullyQualifiedName~GetDueStatesAsync|FullyQualifiedName~ExecutionLease"`; whole Collector Release; API Release filter `FullyQualifiedName~CollectionDispatchStarvationReproductionTests|FullyQualifiedName~CollectionExecutionAcquireEndpointTests`. Expect all pass. Final exact `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`, Release build and workflow solution tests on Ubuntu24/dotnet10.0.202; no --no-build before fresh build. After edits `codegraph sync .` and query changed callers. T3 also covers workflow safety scripts, Chromium, EF model/fresh migration, package vulnerability and local repair checks. Required audit/DDD gates run before implementation release and final acceptance.

Route evidence: existing collection_resume_diagnosis was selected requested gpt-6-luna/high and accepted the read-only followup, returning a repository-informed plan. Model selection does not prove observed model/token usage; telemetry stays null. This turn's skill/reference reads and shared dirty-worktree inspection completed. Code remains untouched until canonical first-write audit passes. Next: audit readiness, release T2, run all verification, then canonical deploy/live comparison; continue through checkpoints.

## Verification record

- First-write release: canonical audit and DDD validation passed before T2 implementation authorization. Validator's initial missing concern-heading and non-null active reviewMode were corrected in planning artifacts before release; no implementation was attempted through a failing gate.
- Read-only baseline06Oct00:25–00:26JST: pipeline unpaused, pipeline GET973ms. Correct DTO projection progress GET5183ms: Realtime due0/running0; Normal due4645/running0, latest completed00:24:16; Background due1189/running0. Initial projection used nonexistent field names and is not evidence of zero work; only corrected DueReady/Running fields are accepted. Stored lifecycle dates are not proof of latest retry attempts.
- Baseline last two hours24 five-minute Lightsail samples: CPU mean9.999956%, maximum10.016834%. No process attribution. GET-only diagnostics used in-memory provider, emitted new allowlisted projections only and retained no configuration/raw response. No pause, retry or deployment yet.
- Remote synchronization: origin/main advanced to643c97e3 through Dependabot upload-artifact v6→v7 only. Lead inspected the exact workflow-only diff and fast-forwarded without touching existing source/test/user edits. Final frozen Ubuntu snapshot uses this base, not the old workflow; worker attribution excludes that remote change.
