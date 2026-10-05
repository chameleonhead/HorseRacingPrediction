# Reduce repeated collection dispatch database work

- Status: Superseded
- Change record schema: 2
- Owner: Collection operations
- Created: 2026-10-05
- Updated: 2026-10-06
- JRA site contract impact: None

## Completion summary

06Oct: the unapproved five-second/index implementation proposal below is superseded by [the user-authorized behavior-preserving query-efficiency design](../20261006_collection-query-efficiency/README.md). Historical investigation and old proposed tasks are retained; they are not approved implementation obligations. Dispatcher interval remains one second and services are not removed.

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | No production/configuration/test changes before explicit design approval. |
| Verification | Not started | Read-only source-shaped SQLite plan is preliminary; actual EF/store, migration and workflow gates required after approval. |
| Deployment/operation | Not started | Read-only metrics collected; no pause/retry/deployment/hosting change for this proposal. |

## Context and evidence

User requests reduction of continued Lightsail CPU load, examination of a one-second loop interval, and appropriate additional indexes. Prior [outbox/race index change](../20261004_collection-outbox-query-index/README.md) explicitly did not claim all backend latency resolved. Prior discovery recovery is separately verified; this is a new bounded design, not its uncompleted code.

05Oct21:28–23:23JST:24 five-minute samples CPU average approximately10%, maximum approximately10.017%; burst capacity approximately0.02345–0.02361%. Twenty-four hourly observations show persistent near10% with temporary capacity recovery while paused before the midday deployment/resume. Correlation does not identify the process or SQL responsible. AWS describes baseline and limited burst behavior in its [official documentation](https://docs.aws.amazon.com/lightsail/latest/userguide/baseline-cpu-performance.html). No plan upgrade is authorized.

23:30JST administration read: unpaused, pipeline GET1821ms, progress GET6954ms. Realtime due0/running0; Normal due4651 with latest terminal completion23:24; Background due1185/running1. Future realtime does not currently prevent all other work. Lifecycle timestamps do not prove latest retry timing. These single request measurements include network/server work and are not a SQL profile.

Existing SSH key authentication was denied; no host process profile/live SQLite plan obtained. AWS/API read-only diagnostics remain available. Secrets were provided in memory only, no raw authenticated response/headers/messages/configuration or credential saved.

Cheap Planner on existing collection_resume_diagnosis, requested gpt-6-luna/high, read-only source and isolated SQL scope, starting09566e95. Observed model/usage unavailable. Lead owns credential boundary, design trade-offs and integration acceptance. No source edits/test authoring delegated during investigation; source references and independent runtime/SQLite evidence are planner inputs, not implementation proof.

## Hypothesis ledger

| ID | Fact boundary / falsification | Result and disposition |
| --- | --- | --- |
| H1 | Dispatcher lacks a wait below one second. Inspect actual loop/options/configuration. | False as stated: default DispatchIntervalSeconds already1; loop clamps minimum1. Changing it to1 alone has no effect. Major other clocks are minute-scale, not tight loops. |
| H2 | Per-lane due selection cannot constrain Lane in the leading existing index. Inspect predicates/index order and compare indexed plans/results. | Existing task index Status,AvailableAt,Lane,Priority uses range before Lane. Source-shaped SQLite3.50.4 with6000 tasks/resources/outboxes across due/future/reserved/dispatched states uses Status/AvailableAt prefix; proposed Status,Lane,AvailableAt,Priority uses Status/Lane/AvailableAt prefix. All three lanes' ordered bounded results equal. Actual EF SQL/provider/live benefit remains unverified. |
| H3 | Another outbox due index is needed. Compare candidate plan with existing correlated index. | Candidate partial due index not selected; do not add. Existing TaskId,DispatchGeneration,DispatchedAt correlated index and Status,LeaseExpiresAt task index retained. |
| H4 | Dispatcher is the entire live CPU cause. Obtain process/profile evidence. | Not established; host authentication denied. Short-cycle database work is an evidenced optimization target, not proof of all CPU attribution. Conditional hold materialization and other services remain possible contributors. |
| H5 | A one-second dispatcher interval is the only avoidable loop work. Inspect all hosted entry points and repeated queries. | False: scheduler materializes all non-Collecting states before due filter/limit; scheduler and watchdog call the same task-lease core; execution-lease reclaim performs Count then equivalent list every dispatch cycle. Source-level redundancies confirmed; live contribution unknown. Revise the pending proposal before approval. |

## Proposed decisions and non-goals

1. Increase dispatcher default and API appsettings CollectionQueue interval from1 to5 seconds, preserving configuration override and existing minimum1. This reduces maximum empty-cycle frequency from60 to12/minute when operation duration is negligible; it does not promise an80% total CPU reduction. Worst-case polling component of additional dispatch and lease-reclaim latency is approximately4 seconds; execution time/queue/aggregation latency remains separate. Maximum cycle-based dispatch throughput also decreases; currently one in-flight envelope and minute-scale tasks limit that benefit/trade-off. Cancellation remains interruptible. No deployed override is proven; deployment must read back only the effective numeric setting, not raw environment.
2. Add one nonunique task index `(Status,Lane,AvailableAt,Priority)` to fresh EF model and additive transactional schema24 migration. Retain old indexes and all task/outbox/history data. Require actual EF-generated query plan and exact ordered real-store result parity before accepting the candidate. Reject/remove the unshipped candidate if that proof fails; no blind index sweep.
3. Preserve allocation, capacity, leases, reservation/cardinality guards, holds, priority, future availability and duplicate prevention. Do not rewrite lease reclaim/count behavior or conditional hold identity materialization in this bounded change. No domain/JRA parsing/URL/API contract or UI change.
4. Use canonical app-deploy only after matching Ubuntu workflow gates. Preserve pause/drain/backup/health and notification evidence. Resume only through guarded existing API if deployment preserves pause and safety review allows it; no bulk recovery or hold clearing.
5. Compare post-deploy CPU/burst metrics, bounded API latency and actual lane attempts with the recorded baseline. Lower repeated-cycle frequency and verified indexed lookup are the guaranteed mechanical outcomes, not complete elimination of CPU demand. Persistent saturation requires evidence-led continued diagnosis, not a false completion claim or silent hosting upgrade.

## Concern and agreement ledger

| ID | Evidence and impact | Recommendation / alternative / remaining risk | AC/task | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- |
| C1 | Current dispatcher already1 second; changing to5 increases polling/lease-reclaim delay by up to approximately4seconds and lowers maximum cycle-based throughput. | Accept bounded delay to reduce repeated DB work; alternate keep1 and index-only offers less repeat-load relief. Keep minimum/override and interruptible stop. Reconsider if observed race-odds/dispatch freshness or due-backlog throughput regresses. | AC1/T2,T4,T5 | Recommend5 seconds, no all-clock slowdown. | Await explicit proposal approval. | Resolved in design |
| C2 | Live SQL/process attribution unavailable; SQLite synthetic plan is not actual EF/provider or live timing. | Gate index on actual SQL/ordered store parity and observe production; do not promise all CPU resolved. Alternative blind changes rejected. Unexplained persistent saturation stays a diagnosis item. | AC2–AC3/T3–T5 | Preserve uncertainty and executable evidence. | Await explicit proposal approval. | Resolved in design |
| C3 | Extra index adds write/storage cost and migration CPU on a throttled1GB host. | One nonunique additive index only; idempotence/data preservation, pause/drain/backup and health. Failure leaves paused for operator-controlled recovery, no automatic rollback claim. | AC2–AC4/T3–T5 | No old-index deletion, destructive restore or hosting cost increase. | Await explicit proposal approval. | Resolved in design |
| C4 | Additional requested loop audit found stronger repeat-work candidates than interval-only mitigation: scheduler memory filtering and duplicate reclaim paths. | Before asking approval, revise bounded tasks/ACs to evaluate SQL filtering and count/list consolidation without removing recovery/safety behavior. Source facts are established but provider/result/CPU proof is not. Previous5-second proposal is not the settled implementation contract. | AC1–AC4/T1–T5 | Prioritize duplicate work over indiscriminate slower polling. | User requested investigation only; new implementation scope not approved. | Open decision |

Inspected requirement conflicts, safety/data/privacy, query correctness, migration/concurrency/recovery, stale verification, model telemetry and review burden. Subsequent loop investigation opens C4: this proposal needs revision before approval is requested again. This record remains Proposed; no interval/index/reclaim/query change has been implemented or approved.

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Default dispatch delay5 seconds, existing minimum1/override and interruptible shutdown preserved; no fairness/future/pause/hold/capacity/lease/duplicate semantics change. | T2,T4,T5 | Loop timing/configuration and existing real dispatcher regressions; actual deployment configuration read-back. | Not started |
| AC2 | Fresh and genuine schema23 upgrade contain the exact nonunique index, idempotent migration and unchanged rows/history; actual EF/store query uses Status/Lane/AvailableAt prefix with exact indexed/unindexed ordered results across all lanes and guarded cases. | T3,T4 | SQLite provider SQL/EXPLAIN/PRAGMA and store/migration counterexamples. | Not started |
| AC3 | Exact source CI/deploy terminal successful and health valid; record post-deploy CPU/burst/latency and continuing eligible lane attempts, distinguishing mechanical reduction from any unresolved CPU attribution. | T4,T5 | Matching Ubuntu gates, terminal annotations and bounded live metrics/read-back/attempts. | Not started |
| AC4 | No secret/raw output, data deletion, failure dismissal, bulk retry, hold clearing, hosting changes or unrelated edits mixed into commits. | T1–T5 | Attributed diff, audit/DDD gates and operation ledger. | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Integrate read-only investigation and safety design. | Lead | Lead | User request | This record | Metrics/source/isolated plan evidence, concern review | Source-shaped plan/prefix and three-lane parity reviewed with exact source predicates; runtime evidence separately captured | Verified | Security, architecture and integration decision ownership | none | unavailable; retries 0; corrections 0; reviews 1 |
| T2 | Implement only approved dispatcher interval/configuration regression. | collection_resume_diagnosis | Luna high | User approval/readiness,T1 | CollectionQueueOptions.cs; API appsettings.json; CollectionPlatformOutboxDispatcherTests.cs | Timing/default/minimum/override/cancellation plus dispatcher regressions | Not dispatched | Proposed | Bounded serial mechanical executor; material contract changes return Lead | none | unavailable; retries 0; corrections 0; reviews 0 |
| T3 | Add lane-leading index and provider/migration/guard proofs. | collection_resume_diagnosis | Luna high | User approval/readiness,T1,T2 serial ownership | CollectionPlatformDbContext.cs; CollectionPlatformSchemaMigrator.cs; CollectionDispatchStarvationReproductionTests.cs; current-schema/store fixtures identified before release | Actual EF SQL/ordered parity/metadata/data preservation plus dispatch/lease/hold suites | Preliminary source-shaped plan only, not implementation proof | Proposed | Bounded additive implementation; persistence decision remains Lead | none | unavailable; retries 0; corrections 0; reviews 0 |
| T4 | Verify final frozen source with exact Ubuntu workflow gates. | verify_deploy_guards | Luna high | T2,T3 | Read-only repo/isolated container | Exact restore/format/build/Chromium/EF/tests/scripts; graph/hash parity | Not dispatched | Proposed | Mechanical verifier, no source writes | none | unavailable; retries 0; corrections 0; reviews 0 |
| T5 | Canonical deployment and safe live comparison/final acceptance. | Lead | Lead | T4 | This record; canonical workflow and guarded pipeline operation | Exact-SHA terminal runs/annotations, health/configuration/metrics/eligible attempts | No mutation performed | Proposed | Security-sensitive production operations and final acceptance | none | unavailable; retries 0; corrections 0; reviews 0 |

## Documentation updates

This record is the design and performance-evidence source. Prior index record remains historical and correctly excludes full latency resolution. docs/26-collection-platform-design.md links this pending proposal and accurately retains the current1-second setting until implementation. The approved/provisioned interval/index truth will be reconciled at completion. No UI/API/public-site documentation contract change.

- evidence/service-loop-inventory.md: read-only source/runtime-boundary inventory and duplicate-work findings supporting reconsideration of the proposal. It is investigation evidence, not a new canonical scheduler contract.

## Verification plan and review

Read-only planner completed without file changes. Lead independently inspected options/pending predicates/dispatcher call path with CodeGraph and rejected any all-table-scan or all-CPU-cause claim. Lane predicate follows a range column in the current task index; source-shaped plan establishes a falsifiable candidate only. Scheduler/planner1minute, watchdog/backfill5minutes, alert5seconds, DLQ30seconds; event-driven telemetry50ms batching is not a periodic table-scan loop. Existing correlated outbox and lease-expiration indexes are retained. Conditional active-hold materialization and uncapped lookback flow monitoring are diagnosis candidates, not verified hot spots or approved rewrites.

Implementation order: T2 interval plus deterministic loop tests; T3 exact actual EF provider SQL/index/ordered store parity with schema23/fresh upgrade preservation; T4 frozen complete Ubuntu checkout; T5 canonical deployment/effective setting/health/CPU and lane comparison. Source/test scopes expanded for genuine current-version fixtures must be enumerated before their edit; historical schema23 fixtures remain genuine. No new abstraction or unrelated optimization.

Focused commands: `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --configuration Release --filter "FullyQualifiedName~CollectionPlatformOutboxDispatcherTests|FullyQualifiedName~CollectionDispatchStarvationReproductionTests|FullyQualifiedName~CollectionDispatchTelemetryTests|FullyQualifiedName~CollectionDispatchTelemetrySnapshotQueryTests"`; Collector project Release filter `FullyQualifiedName~CollectionPlatformStoreTests`. Expected all pass with deterministic delay/cancellation proof; indexed/unindexed exact IDs/order across future tasks, stale generations, duplicate outboxes, active holds and leases; PRAGMA exact nonunique columns and preserved rows/history. Then exact workflow restore/format/build/Chromium/EF/solution tests, safety scripts and vulnerability/annotation checks on Ubuntu24, not Windows-only claims. Post-deploy bounded15-minute metrics compare with above baseline, numeric effective interval read-back and actual eligible task attempts; reassess if saturation/latency or dispatch freshness worsens. No old fixture coverage or synthetic timing ratio substitutes for these gates.

## Readiness and next action

追加の常駐ループ調査は[サービス一覧と重複処理の証拠](evidence/service-loop-inventory.md)を参照。定義9 HostedService（API最大8/Predictor1）、別枠Collector local CLI1・画面更新2。Leadが登録・呼出し・空受信の反例を独立照合し、調査はVerified、プロダクション変更なし。特に毎分GetDueStatesの全行materialization後の期限/上限判定、scheduler/watchdogの同一task-lease回収、毎秒execution-lease二重検索を新たに確認した。CPU全体への寄与率は未計測。単なる5秒化より重複とSQL側絞込みを先に評価すべき根拠になり、以前のinterval/index案をそのまま承認済みとして実装しない。次の実装承認依頼前に、これらの改善候補を反例・安全性・タスク/ACへ落として案を見直す必要がある。既存T2–T5は未承認Proposedのまま、今回の依頼は調査・報告であって実装承認ではない。

Lead loaded orchestration, DDD, production recovery and required references; requested existing Luna/high read-only planner route. No implementation-write release, no audit claim of completed implementation and no approved change yet. Unrelated AGENTS/orchestration policy/routing files are preserved. Complete planner configuration/test/reference inventory, validate this proposal, present every AC and the5-second delay/index/profile limitations for explicit approval. After approval record exact file scopes, worker contracts and passing canonical audit before first edit; then implement, test, deploy and measure without stopping at commits.
