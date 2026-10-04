# Reduce collection outbox and race identity lookup cost

- Status: Approved
- Change record schema: 2
- Owner: Collection operations
- Created: 2026-10-04
- Updated: 2026-10-05

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Complete | T2/T4 index-only model/migration/provider proofs and T6 current-schema expectations verified; no scheduling/identity behavior changes. |
| Verification | Complete | Final hash-matched Ubuntu format/build/Chromium/EF and both full solution test commands pass1,620/0fail/1baseline-skip. Focused proofs and migration parity pass. |
| Deployment/operation | Partial | Temporary processing verified: Normal succeeded 22:53; Background Running 1 observed 22:59–23:01. Permanent optimization not deployed. |

## Context

[Incident evidence](../../incidents/20261004_stalled-collection.md) records an unpaused pipeline with long idle gaps.
AWS confirms Lambda and its SQS trigger are enabled, queue depth zero at the sampled cutoff, and subsequent
Normal successes and Background attempts. The Lightsail micro_3_0 instance has 2 vCPU/1 GB, CPU near its 10%
baseline, and CPU burst capacity approximately 0.023% (~2 seconds). Lambda recorded a 57-second race operation
with about 50 seconds in three API calls. These facts establish very limited CPU headroom and backend latency;
they do not establish which exact SQL statement accounts for that latency.

The deployed source's outbox model/migrator lacks an index matching repeated correlated lookups of
`TaskId`, `DispatchGeneration`, and `DispatchedAt`. Pending selection, reservation validation, cardinality guards,
and acquisition repeatedly use those predicates. Existing indexes lead with DispatchedAt and cannot efficiently
identify one task/generation within all undispatched rows.

## Goals and frozen boundaries

- Add the exact-key index without changing dispatch eligibility, lane allocation, capacity, holds, cardinality guards,
  reservation lifetime/token fencing, or task retry behavior.
- Provision it for fresh databases and existing schema-22 databases with an additive, transactional migration.
- Verify normal/background work continues through the deployed path. Future Realtime work remains future-dated.
- Keep the current hosting plan. No queue deletion, failure dismissal, task cancellation, or bulk retries.

Trainer navigation timeouts and progress timestamps that summarize first task acquisition rather than latest attempt
are separate observed issues; this index proposal does not claim to resolve them. Lead retains those diagnosis items
in the incident record. The final report must distinguish improved dispatch from those remaining issues.

## Hypothesis ledger

| ID | Claim and fact boundary | Supporting/contradicting evidence and falsification | Disposition |
| --- | --- | --- | --- |
| H1 | The repeated outbox count can scan many unrelated Ready outbox rows. Source/local mechanism, not live-plan proof. | Model/migrator has no correlated-key index. In-memory SQLite with 6,000 tasks and ten generations each: existing plan filters TaskId/generation after DispatchedAt lookup. Median 6.87s for 6,000 probes; hypothetical covering index 21.5ms. All results equal. Actual live row mix/plan remains unknown. | Add provider-shaped plan/result verification; do not extrapolate benchmark ratio to production. |
| H2 | A covering index can preserve guard results. Locally supported, broader provider validation pending. | All 6,000 synthetic counts equal 1 before/after. Verify duplicates, stale generations, dispatched rows, leases/holds, and mixed lanes through real store tests. | AC1–AC3. |
| H3 | The index alone resolves all backend latency. Unproven. | CPU credits almost exhausted and API calls slow; no live statement-level profile. Other domain writes, monitoring, and task navigation may contribute. | No such guarantee. Measure post-deploy progression/latency and continue investigation if unchanged. |

## Decisions

1. Add a non-unique covering index `(TaskId, DispatchGeneration, DispatchedAt)` to collection_task_outbox.
2. Add schema migration 23 with `CREATE INDEX IF NOT EXISTS`; keep all rows, existing indexes, and history.
3. Match fresh EF model provisioning and migration provisioning. Update assertions meaning “current schema” while
   preserving fixtures genuinely starting at v18/v19/v22.
4. Use the canonical app-deploy workflow after tests. Preserve its pause/drain and health checks. As part of the
   explicitly authorized incident recovery, resume through the existing API only after successful deployment and
   safety review; retain the four isolated identification failure notifications.
5. Add non-unique `(RaceDate, RaceNumber)` on RacePredictionContexts through its separate EventStore EF model/migration/snapshot path. It must retain multiple courses/races sharing a date and number; it must not change normalization or identity selection.

Approval basis: after the explicit outbox proposal and the other-search audit recommended prioritizing outbox plus race identity, the user requested implementation on 2026-10-05. This approves those two additive indexes and the existing data-preservation, regression and production-verification boundaries. Lower-priority history indexes were exploratory candidates, not diagnosed defects. Full-materialization search rewrites remain a separate design workstream; this approval is not represented as resolving every slow query.

Race hypothesis: both identity resolvers use SQL equality on RaceDate/RaceNumber. Current model, snapshot and migrations provide only RaceId PK. In-memory SQLite with 60,000 rows changed SCAN to equality SEARCH with identical results. Live row mix/benefit remains unknown; no production timing guarantee is approved.

## Concern and agreement ledger

| ID | Concern/evidence and impact | Treatment and residual risk | AC/task | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- |
| C1 | The live query plan is unavailable; index benefit may not cover all slow API work. | Preserve semantics, verify actual EF SQL on representative seeded data, compare production observations. Continue the incident if progression remains slow. No claim of complete latency recovery. | AC2–AC4/T1–T3 | Recommend bounded optimization; do not increase hosting cost by assumption. | Approved 2026-10-05 | Accepted risk |
| C2 | Index creation uses CPU/storage and can delay startup on a throttled host. | Additive transactional migration, idempotence/data-preservation tests, canonical pause/drain deployment, monitor migration/startup and health. If migration fails, keep pause and use existing rollback route; do not downgrade schema history or delete data. | AC1/AC4/AC5/T2–T4 | Preserve recovery and failure evidence. | Approved 2026-10-05 | Resolved in design |
| C3 | Trainer transient failures may still dominate higher-priority Background work. | Keep current policy; verify acquisition independently of successful scraping. Record those failures separately rather than broad retry/suppression. | AC3–AC4/T2–T3 | Scope remains two indexes, no query/identity behavior rewrite. | Approved 2026-10-05 | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Fresh and genuine v22 databases contain the covering index; migration is idempotent and task/outbox/history values are preserved. | T2,T6 | Migration/model tests and read-back assertions | Verified |
| AC2 | Actual EF/provider-shaped lookup uses the new index; representative seeded results match the unindexed baseline, including duplicates, stale generations and dispatched rows. | T2 | EXPLAIN QUERY PLAN, exact result comparison, benchmark without flaky wall-clock assertions | Verified |
| AC3 | All existing due/future lane allocation, capacity, hold, lease and reservation/cardinality guards still pass. | T2,T5,T6 | Relevant dispatcher/store/telemetry regression suites, build and formatting gate | Verified |
| AC4 | Exact commit deploy passes migration/startup and health; pipeline resumes safely; post-deploy Normal task success and Background acquisition are observed, with latency and gaps compared to incident baseline. | T3,T5 | CI gates, workflow terminal logs plus API/CloudWatch observations | Not started |
| AC5 | Fresh and upgraded EventStore databases have a non-unique RaceDate/RaceNumber index, unchanged race data and identity results including same-date/number multiple courses; provider equality query uses index. | T4 | EF migration/pending-model checks, model and migration tests, identity regressions and EXPLAIN parity | Verified |

## Documentation updates

- This record is the index proposal and acceptance source. The incident note remains the canonical runtime evidence.
- Inspected collector design and prior dispatcher resilience record: lane/safety/public contracts are unchanged; no
  architecture or user-facing document changes are required for an internal index. Record the resulting schema/index
  verification here rather than duplicate collector behavior descriptions.

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T2 | Model index, migration 23, schema assertions, provider proof and regressions, AC1–AC3 (former T1 combined). | collection_resume_diagnosis | Luna high | Approval and readiness gate | CollectionPlatformDbContext.cs; CollectionPlatformSchemaMigrator.cs; CollectionDispatchStarvationReproductionTests.cs | Focused provider and dispatcher/store/telemetry regression tests; audit (global build/format on T5) | Focused41/41 and21/21 pass again after six-line formatting-only correction; local exact solution formatter pass; Ubuntu integration pending | Verified | One serialized persistence owner | T2-A1,T2-A2 | unavailable; retries 1; corrections 2; reviews 2 |
| T3 | Integrate, deploy and verify real production progress, AC4. | Lead | Lead | T2, T4 and T5 verified | This record and incident evidence; canonical GitHub workflow/API operations | Exact-SHA workflow terminal result, health, acquire/completion observation | Source checkpoints accepted; exact-SHA workflow, health and timestamped acquisition/completion observations pending | In progress | Security-sensitive deployment/integration and final acceptance | none | unavailable; retries 0; corrections 0; reviews 1 |
| T4 | Add race identity model/EF migration and tests, AC5. | collection_resume_diagnosis | Luna high | T2 focused checkpoint | EventStoreDbContext.cs; 20261004163306_AddRaceIdentityLookupIndex.cs; 20261004163306_AddRaceIdentityLookupIndex.Designer.cs; EventStoreDbContextModelSnapshot.cs; SharedCollectionIdentityTests.cs; SqliteDbContextProviderTests.cs | Real provider migration, EXPLAIN and identity regression tests | Identity7/7, provider9/9; no pending model changes; fresh DB update; parity and migration preservation tests, Lead diff acceptance | Verified | Serialized index-only persistence implementation | T4-A1 | unavailable; retries 1; corrections 0; reviews 1 |
| T5 | Run isolated Ubuntu solution CI/format/build/EF/tests, AC3–AC4 prerequisites. | verify_deploy_guards | Luna high | T2 and T4 frozen | read-only repository; ephemeral Ubuntu container workspace | Exact workflow tools/restore/format/Release build/Chromium/EF/full non-External tests, bounded streamed commands | Hash13/13; all gates pass; exact CI/deploy suite each1,620pass0fail1baseline-skip | Verified | Bounded mechanical CI verifier, source corrections return to executor | T5-A1 | unavailable; retries 3; corrections 2; reviews 1 |
| T6 | Close current-schema Collector test expectations, AC1/AC3. | collection_resume_diagnosis | Luna high | F5 source diagnosis | tests/HorseRacingPrediction.Collector.Tests/CollectionPlatform/CollectionPlatformStoreTests.cs | Affected startup tests, full Collector suite, original Ubuntu solution rerun via T5 | Targeted3/3, full Collector Release421/421, exact formatter pass; historical22 fixture preserved; global rerun T5 pending | Verified | Bounded stale fixture correction only | T6-A1 | unavailable; retries 0; corrections 0; reviews 1 |

## Review gates and delivery

Design/task-split review: Lead reviewed source and the independent synthetic result. One persistence owner avoids
overlapping migration/test writes. Public/safety decisions are frozen. Concern review: C1 is explicit residual uncertainty;
C2/C3 are handled without deleting evidence or changing scheduling. No open design choice remains; user approval is pending.

Pre-implementation review/audit and actual executor dispatch are required after approval. No implementation has begun.
Before committing, run related build/tests and `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`,
audit/change-record validators, CodeGraph sync, and diff/status checks. Include acceptance-critical schema fixtures in the
same checkpoint. Deploy through GitHub Actions, then observe at least a Normal success and a Background acquisition.

## Verification record

- Requested read-only investigator: gpt-6-luna/high; dispatch configuration observed, actual model/tokens unavailable.
- Synthetic Python/SQLite in-memory investigation touched no repository or production data. Median timings are mechanism
  evidence only. Lead independently checked current model/migration index definitions and predicate use.
- Runtime recovery evidence: 22:53 Normal success and Background Running 1 from 22:59:56 through 23:01:30 JST; the latter
  batch ended at 23:02:52 with four transient trainer failures. It proves processing, not successful trainer scraping.
- A local ephemeral SSH credential/diagnostic route was rejected by execution policy twice; no credential file or remote
  command was executed. The rejection returned only “blocked by policy,” without a more specific reason. Canonical
  GitHub deployment credentials are unaffected.

## 2026-10-05 readiness checkpoint

Loaded DDD, orchestration and recovery skills this turn. Existing dirty workflow/skill/AGENTS/test/document edits are unrelated and remain preserved/uncommitted. Requested existing executor `collection_resume_diagnosis` on gpt-6-luna/high; collaboration follow-up accepted. Runtime observed model/tokens unavailable. Read-only exact-file planning precedes implementation; Lead will record the plan/audit and passing canonical audit before releasing first write. T1/T2/T4 execute serially on one owner, T3 waits for all three. No material specification question remains within these frozen indexes. Source edits have not begun.

Baseline authenticated API: pipeline unpaused; Normal last completed 2026-10-05 01:08:31 JST, dueReady 4,769; Background dueReady 1,189. Lane lifecycle timestamps are not latest-attempt proof. An initial local probe used an incorrect pipeline route and wrapper; corrected against current contract and rerun successfully without raw response/header output. GitHub and AWS credentials available; Linux Docker and Ubuntu WSL are present, matching verification feasibility to be checked. Existing CI uses Ubuntu 24.04, .NET 10.0.x, Release build/test (TestCategory!=External), EF pending-model and empty-DB migration gates. No hosting increase is authorized.

Next action: receive exact executor plan, validate readiness/audit, release implementation, then run all related gates and canonical deployment/production verification. Do not stop at a commit or investigation milestone.

Pre-implementation review (Lead, 2026-10-05): accepted Luna's repository-informed plan. Former T1 proof is combined into T2 to avoid redundant audit/review; T4 retains race proof. Exact T2 tests: CollectionDispatchStarvationReproductionTests; current-schema fixtures consumed by existing suites. T4 tests: SharedCollectionIdentityTests and SqliteDbContextProviderTests; model plus new EF migration/designer/snapshot. Implement outbox first, then EventStore on the same owner after a T4 dispatch audit gate. Use actual EF SQL/query plans and read-back parity; stale/dispatched/duplicate rows and multi-course races are required counterexamples. No material specification unknown. Commands: `dotnet test tests/HorseRacingPrediction.Api.Tests --configuration Release --filter FullyQualifiedName~CollectionDispatchStarvationReproductionTests`; related dispatcher/store/telemetry suites; analogous focused Infrastructure/identity tests; workflow-equivalent restore/build/test and formatter; EF pending-model and empty-DB update. Expected all pass, covering-index plans and unchanged results. Current SDK 10.0.202; Docker Linux is available for Ubuntu parity, WSL has no dotnet/pwsh. First write remains gated by canonical audit success. Approval does not prefill test success.

Readiness gate result: first audit run rejected active telemetry objects populated with unavailability labels, null outcome rather than object, and scope text mismatch. Those were local audit-artifact defects, not product failures. Corrected to empty active telemetry/outcome objects and matching scope; original canonical command rerun valid. DDD record validator issues=0. T2 first-write released afterward; additional current-schema test paths must be identified before editing and included in table/audit. Lead is sole owner of record/audit edits.

Deployment preparation: read-only verifier `verify_deploy_guards` dispatched gpt-6-luna/high for the existing Approved pause-notice workflow harness on Ubuntu24.04 Docker and safety sequence inspection. No source writes or coding attribution apply. Prior workflow/harness/doc changes remain unstaged and will not be mixed into index commits; their separate delivery/checkpoint is needed only after verification. Existing actionable failure evidence must remain intact; neither green workflow nor index tests imply the collection has resumed.

Verification environment closure: initial Ubuntu Docker guard-harness invocation using the Windows working-tree mount failed (`set: pipefail\r`). `git ls-files --eol` proves index LF / worktree CRLF for workflow and harness. This is a checkout mismatch, not a guard behavior defect. The verifier repeated the exact harness in a container-only LF-normalized copy representing Ubuntu checkout; all 14 guard/restore cases passed on Ubuntu24.04.4, SDK10.0.202, PowerShell7.6.0, Bash5.2.21. Repository mount remained read-only; no semantic/source edits. Linux staging for final checks must use checkout-normalized files to avoid this mismatch.

Open verification failure F1: original focused CollectionDispatchStarvationReproductionTests run passed 40/41; the Schema19 v18 fixture's final current-version assertion still expected 22. New schema is 23. Executor corrected this assertion in the already-owned file; full original suite rerun is required before closure. No broader behavioral change or model promotion is justified.

F1 closure checkpoint: the original full focused command rerun passed 41/41; related dispatcher/dispatch telemetry/snapshot suites passed 21/21, diff check passed, CodeGraph sync up-to-date. Lead reviewed the attributable three-file diff by AC1–AC3. Index-only migration is bounded, and duplicate row insertion proves nonunique intent. Before completion, Lead requested two local evidence closures: compare the same actual store result with the index dropped in an isolated test DB (not only expected eligible IDs), and verify task/history preservation plus exact PRAGMA index columns/uniqueness. These are acceptance-proof corrections, not a design change or escalation. T2 remains In progress; T4 remains Dependent.

Deployment safety clarification from read-only workflow review: current canonical workflow provides stopped-API/WAL validation and dated backups of all three DBs, but has no automated rollback step. Failure leaves collection paused; recovery is operator-controlled using previous image and verified backup with explicit authority for any destructive restore. Do not claim an automatic rollback exists or downgrade migration history. The two additive indexes do not require automatic data rewriting/restore in the normal path.

T2 checkpoint acceptance (Lead): strengthened actual-store indexed/unindexed exact ordered TaskId parity passed; PRAGMA verifies full nonunique index and exact column order; migrated task, outbox and preexisting schema-history values/timestamps preserved with only one v23 append. Final same focused suite 41/41 and related regressions 21/21 pass; F1 closed. Source attribution exactly three owned files. One internal assertion retry, one AC-group evidence correction request, one integrated review; no promotion/escaped defect known. Global Ubuntu solution build/format/full-suite gates are explicitly T3 integration work; T2 is a verified focused slice, not a claim deployment is done. T4 frontier now opens on same executor after frozen T2 source checkpoint; audit and validator precede release.

Exact T2 commands: `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --no-restore --filter FullyQualifiedName~CollectionDispatchStarvationReproductionTests` passed41/41; same project `--filter "FullyQualifiedName~CollectionPlatformOutboxDispatcherTests|FullyQualifiedName~CollectionDispatchTelemetryTests|FullyQualifiedName~CollectionDispatchTelemetrySnapshotQueryTests"` passed21/21. These are focused local Debug checks, not the pending Release/Ubuntu solution CI parity gate. T4 first-write released only after canonical audit valid and DDD validator issues0.

Open verification failure F2: full SqliteDbContextProviderTests failed `Migrator_UpgradesPreviousEnsureCreatedSchemaWithoutLosingData`: current-model EnsureCreated already provisions the new index, then legacy schema baselining records through the prior EF migration; generated CreateIndex tried to recreate it. The genuine prior-EF-history upgrade proof passed. Lead approved local AC5 closure within idempotent/additive contract: only the new migration uses SQLite CREATE INDEX IF NOT EXISTS and DROP INDEX IF EXISTS; no legacy bootstrap rewrite. Exact PRAGMA/nonunique column semantics remain test gates. Provider suite passed9/9 after migration correction, then preservation fields were strengthened; rerun original full provider and identity suites after final edit before closing F2. This is a repository-pattern miss, not grounds for model promotion or altered acceptance.

F2 closed after final edit: same full provider suite9/9 and identity suite7/7 passed. T4 model check reports no pending changes; empty SQLite applied through 20261004163306_AddRaceIdentityLookupIndex. Lead's AC5-group review inspected six-file attributable patch: only nonunique index plus EF snapshot/tool-produced metadata, no resolver/old-migration edits. Actual resolver SQL/index plan and dropped-index result parity cover alternate courses and same-course ambiguity; index metadata exactly two ordered columns/full/nonunique; genuine previous-EF-history data preserved. Existing legacy EnsureCreated test is independent bootstrap counterexample. No material design deviation. T2/T4 source checkpoint frozen; read-only Luna verifier now owns Linux isolated CI checks, with source edits returned to executor if a gate fails.

Open verification environment failure F3: T5's first isolated snapshot omitted solution-listed projects under tools/, so original `dotnet restore HorseRacingPrediction.sln` failed MSB3202. Subsequent format/build outputs from that incomplete snapshot are invalid preliminary diagnostics, not product or gate results. Shell stdin also reintroduced CR at its final line. Verifier is correcting only container staging: include all solution/project inputs (explicit membership check), use LF shell script, and stop dependent gates after restore failure. Repeat original restore and every subsequent CI gate in the complete snapshot before closing; no source change justified by this preliminary formatter output. Router keeps this bounded environment correction on Luna; no automatic promotion.

F3 restore portion closed: complete snapshot validates25 solution projects and52 project references, tools restored7s and exact solution restore passed54s. Ubuntu formatter now reproducibly fails six whitespace locations in owned CollectionDispatchStarvationReproductionTests.cs (1489/1491/1496/1498/1502/1504). This is F4, a local formatting defect, not a model/migration behavior failure. T2-A2 reopens only formatter closure on same owner; subsequent gates need a corrected final snapshot and original format command pass. Preserve baseline text-contract inputs (Dockerfiles/infra/deploy/workflows/config as needed), not just compiler project references. T5 must stop dependent stages after failure; no push/deploy before closure.

T2-A2 local closure: same owner split only six same-line initializer assignments in the owned test file. Local original exact solution formatter exit0/no diagnostics; rebuilt starvation41/41 and adjacent21/21 passed. Lead accepted formatting-only scope; no other files changed. F4 still requires Ubuntu exact formatter pass in T5, not inferred from Windows. Complete final snapshot must archive all baseline tracked repository inputs and overlay only approved patches/new migrations. No global source writes occur during read-only verifier run; source checkpoint frozen again.

Final graph checkpoint: `codegraph sync .` already up-to-date; current entrypoint exploration confirms CollectionPlatformSchemaMigrator current23/index DDL, outbox model index, EventStore index/new EF migration, and unchanged CollectionIdentityResolver date/number predicate/course normalization/ambiguity guard. No source writes by verifier. T5 final snapshot is full tracked HEAD plus exactly nine approved tracked source/test/workflow overlays and two new migration files; unrelated dirty policy/docs edits excluded. Preflight25 projects/52 references and guard14/14 passed; full CI stages running.

Ubuntu checkpoint: exact formatter35s pass (F4 closed), Release build19s pass with0 warnings/errors, Chromium dependency/setup250s pass, EF pending-model2s and empty DB update2s pass. Original full solution test command failed after199s:1,617 passed/3 failed/1 skipped of1,621. Open F5a/F5b are stale current-schema expectations in Collector CollectionPlatformStoreTests (MAX=22 and fresh history COUNT version22; runtime current23). Open F5c is stale guard-message expectation in CollectionQueueCutoverContractTests; it belongs to the separate guard change, not the index commit. Luna read-only diagnosis confirms the exact two current-version assertions; intentional Api v22 migration fixture stays22. Lead approves local T6 only those two expectations, no production changes. Guard record owns F5c. Original full Linux command must pass after all closures; individual green reruns do not erase this red run.

Warning classification: EF tool8.0.11/runtime10.0.12 advisory is preexisting in unchanged baseline tool manifest/Infrastructure package refs (git show26cb5253); model/upgrade checks pass, no tool upgrade in index scope. Coverage collector is unavailable in some hosts while seven suites produced attachments; test execution/counts completed. No coverage completeness claim; coverage-tool harmonization is Lead-owned nonblocking follow-up because these ACs require executed regression outcomes, not a coverage threshold. One existing scheduler test skip is not introduced by these indexes; inspect/classify before final closure. No new compiler warnings were produced.

Skip classification closed: unchanged CollectionPlanningSchedulerCadenceTests.cs:15–20 uses Assert.Inconclusive unless RUN_15_MINUTE_CADENCE_TEST=1, explicitly a release-time realtime check. Read-only verifier traced the gate to baseline619ccca8 and body/name3796dc0b; workflows do not set that opt-in variable. One existing skip is expected in normal CI, not new index-related skipped coverage. No skip removal or realtime15-minute probe is required to prove these index-only ACs.

F5 local correction checkpoint: Lead inspected exact two schema assertions and separate guard-contract expectation patch; no product edits and historical Api v22 fixture unchanged. Affected tests3/3 and full Collector Release421/421 pass. Original complete Ubuntu T5 rerun now takes frozen full tracked HEAD2dead588 plus approved source overlays, including both corrected Collector files; exact format/build/EF/full-suite pass remains required to close F5 globally. No push yet.

F6 verification-attribution correction: verifier reported contradictory snapshot/freeze progress after earlier prep-only versus later release instructions. Lead challenged it; verifier identified a stale staged pipeline and stopped it before EF/tests. Prior repeated-stage results are not accepted as final corrected-source evidence. Lead explicitly superseded prep-only, froze current13 approved source files (11 tracked overlays plus2 new migration files), and required normalized host/container SHA256 equality before rerunning all source-dependent gates. Dependency/download cache may be reused; no code or approval change, no push. Router keeps this bounded environment correction on Luna; final acceptance requires exact input attribution, not self-reported success alone.

F5a/F5b/F5c and F6 closed by original exact Ubuntu CI rerun: final13/13 byte-normalized (BOM preserved/CRLF→LF) host/container hashes matched, including Collector fixtures d3e781ce and703cac73. Complete HEAD2dead588 archive,25 projects52 references preflight; guard14/14, restore2s, formatter38s, Release build20s0warnings/errors, Chromium183s, EF no-pending2s and emptyDB13migrations3s all pass. Original app-ci full command passed1,620/0failed/1expected skip in200s (1,621 total). No coverage completeness claim. Verifier is additionally repeating the app-deploy invocation without CI coverage/TRX flags; no source edits. Permanent indexes have not yet been deployed; AC4 remains open.

Final source verification accepted: app-deploy exact `dotnet test HorseRacingPrediction.sln --no-build --configuration Release --filter "TestCategory!=External"` also passed1,620/0fail/1baseline-skip in200s. Lead grouped AC1–AC3/AC5 source evidence with existing migration/bootstrap/store/identity regressions and exact input hash manifest; no worker self-assessment alone used. T5 environment retries3 (incomplete snapshot, first final rerun exposed stale expectations/format, ambiguous stale snapshot attribution); source correction remains separately attributed to T2/T4/T6/guard record. Runtime model/tokens/currency costs unavailable, no efficiency inference. CodeGraph sync up-to-date, audit/DDD/diff gates pass. Guard corrective checkpoint06dd5a9b is separate; remaining index checkpoint will include source/proofs/current-schema fixtures and these records only. Unrelated AGENTS/skills/routing edits remain intentionally uncommitted. Next operations: commit index checkpoint, fast-forward push main, observe exact-SHA canonical run to terminal, then health/safe resume and timestamped per-attempt Normal success/Background acquisition.
