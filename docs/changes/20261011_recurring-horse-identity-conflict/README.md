# Recurring horse identity conflict recovery

- Status: Proposed
- Change record schema: 2
- Owner: Lead
- Created: 2026-10-11
- Updated: 2026-10-13

## Completion summary

Latest child milestone (2026-10-13): local source checkpoints a116cec9 (transient-continuation fix) and a41a4187 (admin preflight page), UI9/9/API67/67/Collector2/2 and source gates pass. P5V full verification runs on existing Luna route. P5B independent local browser host launch rejected before shell; no config/temp/process effects, user environment requested, no bypass. Production login expired and re-login requested. Neither child deployment nor parent all-data migration/recovery is complete; original identity provenance still unknown and no parent write/reset/resume authorized.

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | Migration/correction runtime remains unimplemented. Separately approved bounded preflight backend/API/logging/UI local slices verified through a41a4187, unpushed; full integrated/manual/live acceptance unfinished. Determine identity provenance before selecting a mapping. |
| Verification | Not started | Production-shaped falsifier and endpoint regression coverage are required. |
| Deployment/operation | Not started | Diagnostic child deployed and Tokyo/Kyoto reads verified at379c18a2; original ID provenance and full-data migration remain unresolved. No horse migration or recovery mutation performed. |

## Context and goals

The user requests remediation of recurring failures, most recently `Race/JRA/20261010:Tokyo:3/race-detail`, group `0FB0D54D5B4D2FAA`. The earlier Kyoto 1R failure reported `HorseIdentityConflict`, but sharing a notification group does not prove the new target has the same horse or rejection branch.

The earlier investigation found that the Kyoto horse's stored UUID differs from both current name-derived and source-derived IDs. That difference alone is not proof of unsafe or safe legacy identity adoption. The original registration event, source bindings, and actual parsed source must discriminate hypotheses.

Restore eligible collection work after a verified correction without corrupting existing horse identities or losing data. Preserve the original failure evidence. A passing diagnostic GET is not recovery.

## Frozen safety boundaries

- No name-only merge, unconditional UUID-v5 adoption, guard removal, destructive data rewrite, or blind retry/resume.
- Preserve user-owned changes to orchestration policy and its related files.
- No historical API key in tool arguments, output, artifacts, or commands; credentials use an approved secure input/provider.
- Diagnosis is read-only. Production implementation requires a concrete reviewed design and explicit approval; deployment and recovery must stay within that approved scope.

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | Site login renewed: Tokyo technical details identify ナムラアラバイ / jra-horse:2024100828 and DomainWriteRejected. | Horse/source and actual failure are established, but the resolver sub-branch is not exposed. | Use the captured evidence below; provenance remains C2/C3. | AC1/T1,T2 | Do not infer the horse from the group key. | User renewed site login | Resolved in design |
| C2 | Existing source-less same-name records can represent different horses; prior stored UUID differs from current generators. | A permissive fallback could silently conflate data. | Require source/provenance-backed identity rules and a same-name/different-source counterexample. Alternatives and residual risk depend on T1/T2 facts. | AC2/T3,T4 | Reject unconditional legacy adoption. | Pending concrete design | Open decision |
| C3 | Initial-field acquisition via authenticated site is now verified by deployed diagnostic child; local AWS login remains unavailable but is not needed for this path. | Initial names are known, but no source evidence was returned for the two targets; this does not establish migration eligibility. | Individual acquisition gap closed; full census/source recovery remains C2/C7 and T1/T3. Preserve bounded evidence and no forced name match. | AC1/T2 | Site-first path verified; no authentication bypass | User approved diagnostic child; site login renewed | Resolved in design |
| C5 | Existing canonical Horse event stream may collide with old stream at sequence 1; payloads/snapshots, derived entry IDs and repair fingerprints/sidecars carry coupled evidence. | SQL replacement may break replay, audit verification or erase meaning. | Audit target streams and define exact transformations including sidecars; reject collisions until domain-aware handling is approved. | AC2,AC4/M1,M2,T3,T4 | No blind stream merge/resequence/hash rewrite | Pending target evidence | Open decision |
| C6 | Repair commits event DB before touching separate collection DB; there is no shared transaction. | Interruption can leave domain and job identities inconsistent. | Pause/drain all writers, coherent backup pair and explicit two-store recovery protocol; test interruption before apply. | AC3,AC4/T3-T5 | Existing redirect repair is not physical canonicalization | Pending concrete protocol | Open decision |
| C7 | User requests all-data migration consideration, not only failing horses. Source inventory is not a census of actual production records. | Missing stores or blindly regenerating valid IDs can corrupt unrelated data. | Whole-dataset classification and typed reference closure; keep valid data/IDs unchanged. Reconcile actual schema and counts, including unknown data. | AC2,AC4/M2,T3,T4 | Full coverage, not gratuitous global rekey | User requested comprehensive consideration; exact manifest pending | Open decision |
| C8 | Offline clone conversion retains original data but needs disk capacity, full writer stop and restart gating. | Multiple file replacement is not intrinsically atomic; rollback after new writes may lose data. | Validate capacity/restore; generation-aware stopped cutover and post-resume delta preservation. Alternative in-place multi-step update has greater partial-state risk. | AC3,AC4/T3-T5 | Recommend copy/validate/cutover subject to target facts | Pending concrete execution protocol | Open decision |
| C9 | External queue/consumer delivery may outlive DB pause. | Old-identity payload can reintroduce mismatches after migration. | Inventory producers/consumers/inflight; prove drain/fence and test delayed delivery without purging evidence. | AC2-AC4/M2,T3-T5 | DB pause is not enough | Pending actual delivery evidence | Open decision |
| C10 | UI/API share one process; current status/maintenance objects are in memory and no independent migration runner is deployed. | API-hosted execution loses control during offline cutover and may falsely report success. | Fixed typed admin command, durable journal outside migrated stores, single-purpose host runner and explicit temporary UI disconnect/reconnect. Reject arbitrary shell/Docker socket shortcuts. | AC5/M3,T3,T4,U1,T5 | Command/UI requirement accepted; runner privilege/delivery/cutover must be concretely reviewed | User requests admin invocation; exact runner proposal pending | Open decision |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Establish the actual Tokyo rejection, identity branch, source/provenance facts, and bounded affected scope; distinguish facts from hypotheses. | T1,T2,M4,M5 | Target attempt/batch and live initial-name/source evidence captured; original ID provenance remains unresolved pending bounded whole-data preflight | Connected |
| AC2 | Implement a verified one-time migration and single canonical runtime rather than accumulating permanent legacy identity rules; preserve distinct same-name JRA horses. | M1,M2,T3,T4 | Migration/handler path tests, integrity regressions, prohibited legacy runtime caller inventory. | Not started |
| AC3 | Deploy the verified correction and recover only eligible tasks; prove succeeding replacement work and continued progress. | T5 | Deployment revision, task success, pipeline progress; preserve unrelated stops. | Not started |
| AC4 | Inspect the whole persisted dataset and classify every storage surface; migrate proven old identities and all affected references while preserving valid/unaffected data and domain meaning; no automatic reset or ambiguous merge. | M1,M2,M5,T4,T5 | Source and target inventory reconciliation, preview/coverage, exact before-after/replay checks, interruption/idempotence tests and target verification. | Not started |
| AC5 | An authenticated admin screen invokes an allowlisted migration command, separates preview from execution, displays durable progress/results and restart recovery; rejects duplicate/stale/unsafe execution and does not depend on the browser circuit surviving. | M3,M5,T3,T4,U1,T5 | API/command tests, component states and browser navigation/reconnect/permission verification; actual runner/cutover proof. | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Trace identity provenance, change history, and existing read-only evidence seams; propose bounded corrective plan. | hold_impl | Luna | - | Read-only repository/isolated probes | Cited source paths, commits, and falsifier design | Bounded current/history/compiled recipe investigation accepted; all observed name/stable-source/exact-CNAME candidates differ, actual caller/input unknown and retained underAC1/T3 | Verified | Existing gpt-6-luna/high requested route; observed model unavailable | T1-A1, T1-A2, T1-A3 | unavailable; retries 1; corrections 0; reviews 3 |
| T2 | Capture Tokyo production facts and reconcile hypotheses. | Lead | Lead | Approved diagnostic child T4 | Read-only production | Authenticated target and provenance evidence | Actual Tokyo/Kyoto fields captured after379c18a2 deployment; distinct-NormalizedName hypothesis falsified and source/DOB gaps explicit; historical ID provenance remains T1 | Verified | Credential/risk boundary retained by Lead | none | unavailable; retries 0; corrections 0; reviews 2 |
| M1 | Inventory persisted references, migration capability and canonical runtime paths; return concrete feasibility plan. | hold_impl | Luna | T1 | Read-only repository/isolated probes | Source/typed reference inventory, collision/replay/interruption test plan | Bounded source inventory and plan below; target data and exact complete migration coverage remain T2/T3, not proved here | Verified | Existing gpt-6-luna/high requested route; data contract remains with Lead | M1-A1 | unavailable; retries 0; corrections 0; reviews 1 |
| M2 | Extend M1 to all persistence surfaces and identity producers; reconcile source inventory and propose whole-dataset migration coverage. | hold_impl | Luna | M1 | Read-only repository/isolated probes | Exact stores/table inventory, typed transformation coverage and no-edit status | [47 source-defined application tables, four SQLite stores and external surfaces](persistence-inventory.md); no production census/feasibility claim | Verified | Existing gpt-6-luna/high route; persistence decisions retained by Lead | M2-A1 | unavailable; retries 0; corrections 1; reviews 2 |
| M3 | Inspect existing admin command/UI/auth and independent runner/restart seams. | hold_impl | Luna | M2 | Read-only repository | Cited existing patterns/routes/tests and API-stop/UI-lifetime counterexample | [Admin command design/evidence](admin-migration-command.md): existing UI patterns found; independent runner absent; no implementation | Verified | Existing gpt-6-luna/high route; security/cutover decision retained by Lead | M3-A1 | unavailable; retries 0; corrections 0; reviews 1 |
| M4 | Freeze a bounded diagnostic-only source/provenance command plan, without claiming a migration plan is validated. | hold_impl | Luna | M3 | Read-only repository/local metadata probe | Exact query/DTO/file/test plan and missing-event/same-name/restart counterexamples | [Diagnostic prerequisite](../20261011_horse-identity-diagnostics/README.md); source plan and metadata evidence reviewed, no implementation/target read | Verified | Existing gpt-6-luna/high route; migration/security decisions remain Lead-owned | M4-A1 | unavailable; retries 0; corrections 1; reviews 2 |
| M5 | Draft bounded whole-data migration-preflight acquisition design and UI; do not implement or approve migration. | hold_impl — Cheap Planner | Luna | T1,T2,M2,M3 | Exact preflight proposal and mock files below | Source-informed typed route/coverage/test plan, concern review and proposal validators | [Proposed child](../20261012_horse-identity-migration-preflight/README.md), mock and ordered plan accepted for approval request; all provenance/full-data/cutover gaps stay explicit | Verified | User requests maximal Luna delegation; frozen data/security/public boundaries from Lead | M5-A1 | unavailable; retries 1; corrections 3; reviews 1 |
| T3 | Freeze migration/command/UI contract and settle material concerns/readiness. | Lead | Lead | T1,T2,M1,M2,M3 | This record and linked design only | Source/data coverage, event/runner integrity, UI/route ledger and AC/task review before source edits | User accepted high-level outcome and UI command entry; concrete history/data/runner risks remain unresolved | Dependent | Identity/data/security/public contract and final acceptance owned by Lead | none | unavailable; retries 0; corrections 0; reviews 0 |
| T4 | Implement migration/canonical runtime and mechanically verify. | hold_impl — planned executor/verifier | Luna | T3 | Exact implementation/test files to be frozen from M1/T3; no edits yet | Preview/no-write, idempotence/rollback, replay integrity, actual handler and full regression tests | Former three-file compatibility patch is not the current plan | Dependent | gpt-6-luna/high; serialized exact ownership after planning | none | unavailable; retries 0; corrections 0; reviews 0 |
| U1 | Implement admin migration page/API adapter and verify user-visible states. | hold_impl — planned UI executor/verifier | Luna | T3,T4 | Exact UI/client/component-test files frozen after M3/T3; serialized ownership | Fluent component states and local browser full flow/reconnect/narrow/keyboard checks | UI implementation not started | Dependent | Luna/high; frozen public/UX contract from Lead | none | unavailable; retries 0; corrections 0; reviews 0 |
| T5 | Deploy, perform bounded recovery, independently verify outcome. | Lead — deployment/recovery acceptance; CI — mechanical gates | Lead/CI | T4,U1 | Canonical deployment and approved migration/recovery route frozen in T3; no edits yet | Successful replacement, command/UI operation outcome and continuing progress | Next: after verified code and production eligibility, execute bounded recovery | Dependent | Lead retains credentials, production risk and final integration; workflow gates supply independent checks | none | unavailable; retries 0; corrections 0; reviews 0 |

## Review gates and next action

2026-10-13 acquisition continuation (Lead): the user explicitly approved bounded preflight child AC1–AC5/C1–C6 after the summarized scope and asked to continue. Child source P1/P2/P3/P3L is verified and checkpointed locally atc7fb93ed (not pushed/deployed); P4 UI is now executing on the existing selected Luna route after the originally planned UI worker hit a usage limit. Actual Program/browser/full-solution/normal deployment/bounded production reads remain unfinished. This is an acquisition milestone, not a full target census, migration command, identity-provenance explanation or recovery. Parent AC1 remains Connected and AC2–AC5 Not started, C2/C5–C10 Open, T3/T4/U1/T5 incomplete. Unknown original caller/input and all-data/reference/external/collision/cutover decisions retain Lead ownership. Next finish approved child UI/integrated/live acceptance, then use actual bounded observations to settle concrete parent migration design; no mapping, apply/reset/resume or blind retry authorized by this continuation.

M5 proposal checkpoint (2026-10-12, Lead): integrated child AC1–AC5/concerns reviewed and accepted for explicit approval request, not implementation. Four configured SQLite schemas and six row-readable tables are bounded observation only; full target census, provenance, migration runner/apply/cutover and recovery remain unfinished parent work. Lead independently reran `python scripts/audit_agent_execution.py docs/changes/20261011_recurring-horse-identity-conflict`, canonical DDD validator for parent/diagnostic/preflight children, and `git diff --check`: all exit0, DDD issues0. One worker validator-path retry and three review correction rounds are recorded; usage/model/active-effort telemetry unavailable. No source/test edits, no new production operation. Next action: obtain explicit preflight child approval, then assign exact source/test scope and readiness gate to Luna before implementation; do not migrate/reset/resume meanwhile.

2026-10-12 continuation: user explicitly requests Luna-first delegation to conserve the five-hour usage window; no model-efficiency claim or observed telemetry inferred. M5 existinggpt-6-luna/high Cheap Planner owns only `docs/changes/20261012_horse-identity-migration-preflight/README.md`, `mocks/preflight.md`, `execution-plan.md`, and a strictly labeled Proposed navigation/design supplement in `docs/20-admin-ui-design.md` if needed. Lead owns this parent/audits, security/public/persistence decisions and final acceptance. Plan purpose: use a source-informed, bounded, authenticated, read-only whole-store inventory and paginated horse/reference evidence to prepare a concrete migration; not another single-ID-only workaround, actual apply, privileged runner, reset or resume. Source tables/config/store shapes and existing query/UI/test seams from M2/M3 must be rechecked with CodeGraph before drafting. Mechanical proposal validators/diff and bounded counterexamples are required, no production source edits before separate explicit approval. New record remainsProposed; separate-store/non-atomic reads, unknown/unrecoverable evidence, external/files not inspected and incomplete coverage must be explicit blockers, never falsely labeled a migration-ready manifest. Prefer existing patterns, no generalized migration framework or new short polling loop. Next M5 draft → Lead concern/public contract review → exact user approval request, then ordinary implementation/test work stays on Luna. Parent remainsProposed.

Production diagnostics after successful matching deployment38117330991/CI38117331016 at379c18a2: Tokyo requestedhorse-d198b3e2-771f-5dcb-94dc-a739e98b6119, current/initial RegisteredName and NormalizedName allナムラアラバイ, births absent, current/registration Supported; fourUTC observations06:27:27. LegacyNormalizedName and CurrentNameOnly both calculatehorse-b9a88711-1650-55d4-b157-467e4c438511, bothDiffers. Kyoto requestedhorse-ba9e991c-56e3-5ecd-a392-69d125afed42, current/initial names allノヴァアクレイム, births absent, current/registration Supported; fourUTC observations06:27:36. Both name calculations yieldhorse-7891f56d-3ecd-5af0-8b2f-7dfabe9e79ae, bothDiffers. Each source evidence list has onlyProfileReadModel Missing/sourceUrl Missing; no collection-origin row displayed within approved diagnostic scope. This is not a full DB census or proof that no other historical source exists.

Hypothesis disposition: these production fields contradict the distinct initial NormalizedName hypothesis for both observed IDs. Do not apply its compatibility fix or claim migration eligibility. C3's initial-field acquisition gap is closed, but historical ID provenance and source linkage remain unresolved under C2/T1/T2. Reopen T1 on existing preferred Luna read-only route: CodeGraph-first current/historical generator/caller trace and bounded in-memory comparisons of actual names/known failure-sourceIDs against source-defined historic recipes, no blind candidate search, file edits, production APIs or mutation. Return cited source/commits and exact matching/nonmatching inputs; a match alone is not identity proof. Lead owns data/public/risk decisions. Existing source/read-only scope unchanged; no new production implementation authorization. Next finish T1 evidence, reconcile T2 facts and migration acquisition/runner design rather than force matching.

Independent source recovery (Lead browser, read-only): production Tokyo job shows actual requested raceURL `https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1005202604030320261010/51`. That official page visibly identifies2026-10-10 Tokyo3R and rowナムラアラバイ/horse number9 links to `https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002024100828/2E`. Following its visible Kyoto3-day and1R links gives official `accessS.html?CNAME=pw01sde1008202604030120261010/C5`, heading2026-10-10 Kyoto1R, andノヴァアクレイム link `https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002024108210/D8`. These are observed links, not reconstructed checksums, guessed variants or a JRA write/betting action. T1-A3 can now test the exact raw-CNAME historical recipe; the earlier profile Missing observation still stands. Current public links alone are not a production timestamped old-writer record or whole-data mapping proof.

T1-A3 closure: actual compiled shared helpers produce Tokyo stable-sourcehorse-9ffccf98-2fb3-57d1-9e20-fa0efd9c962c and exact-CNAMEhorse-c4bef1c9-a121-570b-bb96-63acab861a33; Kyoto stable-sourcehorse-bd4c07df-8a9a-510e-a754-9dc1123ebb63 and exact-CNAMEhorse-70618520-1c05-5e74-986a-f10cb0fc87bb. None match either storedID. Actual official card links (TokyoaccessD95, KyotoaccessD09) are identical to result links. DOM codepoint checks confirm exact trimmed initial/current strings, including precomposedヴ, not just visual equality. Namespace1c86504c-11bb-4e95-b997-94d64f0569f3 and SHA1/byte-order implementation stable fromf3d2065d; name fallback input changed atf2f0988c and5c3a066c, but these literal names are unchanged by both recipes. be6a5713 changed rawCNAME→stable source normalization;0d3c96e8 added exact legacy-route recognition. These changes are confirmed history, not the demonstrated origin of these IDs.

Provenance limitation independently accepted: HorseRegistered's payload stores names/profile fields, not source URL/producer/derivation key (Lead CodeGraph verified actual source). Explicit caller-supplied ID paths exist but no evidence proves their use here. Envelope metadata/old caller logs/other persisted race references may help, but the full registration payload is not guaranteed to recover missing input. Do not treat another raw-event dump as a known fix. Historical route variant remains an unproven possibility, not a recipe to brute-force. T1 bounded investigation Verified; AC1 remains Connected and T3 unresolved pending whole-data/creation-reference evidence. Reflection out-parameter setup failed once, corrected in-memory probe passed; no source/test edits or build reruns, unchanged source and matching completed CI supply regression/scope evidence. Lead group acceptance, unavailable observed-model/usage/effort, no cost inference; a failed Lead --depth graph flag was corrected with documented explore and is not a worker/source retry.

Historical acquisition checkpoint (superseded by the completed deployment/read evidence above): diagnostic child code and local verification were accepted (final solution Release exit0, API507 passed/8 skipped, real browser desktop/narrow/genuine input/refresh evidence), while matching app-deploy38117330991 and target reads were pending. Those dependencies are now closed. Do not attribute the incident to caught diagnostic implementation/test-fixture defects or synthetic legacy-input tests. No parent migration/apply/reset/retry/resume has occurred; parent remains Proposed and all migration/runner/cutover concerns retain their previous disposition.

Continuation checkpoint: the [approved diagnostic child](../20261011_horse-identity-diagnostics/README.md) has completed its backend-only checkpoint (API496pass/8skip/504total, Collector469pass, focused9/9+1/1 and formatter/graph); UI/SDK integration, whole Release/browser and canonical deployment/target reads remain active. These are acquisition milestones, not migration feasibility or production recovery. Parent remains Proposed with C2/C3/C5-C10 unsettled and ACs unfinished. T2 now depends on child T4; no local AWS-login requirement is inferred. Next: finish the child and read actual initial-registration/source evidence, then reconcile hypotheses and freeze whole-data command/runner/coverage/cutover design for explicit approval. No migration, reset, retry or resume was performed.

Design/task-split review is incomplete until T1/T2 establish the actual incident. No production source edits, retry, resume, or data migration are authorized by this proposal. The user has requested remediation, not yet approved a concrete identity-data contract.

Next: obtain bounded initial-registration/source evidence through an existing authorized read path or a concretely reviewed read-only diagnostic capability. Then reconcile the corrective plan and freeze exact files, tests, rollback/data safeguards, and production verification before requesting approval. Site failure reads are complete; do not require AWS login for those reads or assume it is required by GitHub deployment.

## Repository-informed candidate plan (not approved)

Historical candidate below is superseded as an implementation recommendation by the user's migration-first preference. Its source findings remain evidence; do not execute the compatibility-only patch.

Historical `HttpDataCollectionWriteService.UpsertHorseAsync` generated IDs using the fixed horse UUID-v5 namespace and `Trim(normalizedName ?? registeredName)`. `HorseRegistered` preserves both names. Current `CollectionIdentityResolver.LoadHorsesAsync` reconstructs legacy identity from sequence-1 `RegisteredName` only, so an old caller-normalized key can be missed even when canonical identity names are equivalent.

An isolated call to the existing Release resolver reproduced rejection for an old normalized-name-derived UUID, and acceptance of the original ID when its name-derived proof flag is true. This is a synthetic counterexample, **not** evidence that Tokyo 3R or the prior Kyoto horse has this provenance.

Candidate correction: recognize the exact historical UUID from immutable sequence-1 `NormalizedName` only when its canonical identity name equals the current registered name. Preserve all existing source binding, unique candidate, same-name competitor, and conflict guards. Never allow arbitrary UUID-v5 adoption. No data migration, redirect, merge, deletion, history rewrite, or isolation-policy change is proposed.

Candidate source/test scope after approval:

- `src/HorseRacingPrediction.Api/CollectionIdentityResolver.cs`
- `tests/HorseRacingPrediction.Api.Tests/CollectionIdentityResolverTests.cs`
- `tests/HorseRacingPrediction.Api.Tests/CollectedRaceIdentityGuardTests.cs`

Ordered steps: confirm actual incident; freeze proof contract; approve and pass readiness audit; add classification and actual result-bulk endpoint regressions; implement exact proof; run focused and full API regression suites; deploy verified revision; retry only proven eligible failures; verify replacement success and continued progress. No source edits have begun.

Required counterexamples: arbitrary same-name UUID-v5 still rejects without writes; different official JRA IDs with the same name remain separate; mismatched normalized name, existing conflicting source, and ambiguous candidates remain protected. The endpoint positive case must reuse the original HorseId and emit no duplicate `HorseRegistered`.

Verification commands after implementation: `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --configuration Release --filter "FullyQualifiedName~CollectionIdentityResolverTests|FullyQualifiedName~CollectedRaceIdentityGuardTests"`, then the full Release API test project. Expected: all tests pass, including real result-bulk pre-write rejection and original-ID preservation. Deployment parity gates and production evidence remain mandatory.

Evidence gap: no generic event-history UI/API exposes sequence-1 `NormalizedName`. Existing profile/resource reads expose source binding only when present, not original registration provenance. A secure bounded data read or explicitly approved diagnostic capability may be needed; no guessed event endpoint or new API is introduced here.

## Causal history analysis — 2026-10-11

Confirmed source history: commit `f2f0988ce8b1c788d3c7f01a755c4b90ea1cbf71` (2026-09-13 23:27 JST) changed the then-Collector `HttpDataCollectionWriteService.UpsertHorseProfileCoreAsync` from `BuildEntityId("horse", NormalizeDisplayName(normalizedName ?? registeredName))` to `BuildHorseId(registeredName, jraSourceIdentity)`. The horse namespace did not change. JRA source-based IDs intentionally differ from name-based IDs; that difference alone is not a defect or evidence of database corruption.

The compatibility defect shape is incomplete recognition of historical inputs: current `LoadHorsesAsync` reads the first registration's `RegisteredName`, not its persisted `NormalizedName`. An old row generated from a distinct caller-supplied normalized-name key can therefore lack the proof flag even when current identity-name normalization considers the names equivalent. The resolver then correctly refuses an unproven same-name UUID-v5 candidate, preventing unsafe name-only conflation.

Test gap: existing resolver/guard coverage exercises proof flags, UUID-v4 adoption, namesakes, ambiguity, and source-route compatibility, but not the loader-plus-endpoint path starting with the exact old writer's distinct `NormalizedName` and stored UUID. Passing these tests does not cover every historical generator input. This is a demonstrated coverage gap, not proof of who made a mistake or why a review decision was made.

Production boundary: the current race-card caller supplies `normalizedName: null`, so it cannot establish that Tokyo's row came from a distinct normalized-name input. The marker-name probe is synthetic. Original Tokyo event, HorseId, and source bindings are still required before attributing this incident to the gap. No evidence currently establishes an incorrect JRA URL or random UUID corruption.

Repeated requests under unchanged inputs/state encounter the same deterministic integrity guard; retry is not a correction. Proposed prevention is exact historical-input proof plus an old-writer-shaped loader/REST regression, preserving unknown/source-conflict stops.

Independent review: Lead inspected the historical writer diff with `git show f2f0988c -- ':(glob)**/HttpDataCollectionWriteService.cs'` and current resolver lines 34-71, confirming the changed ID input and omitted event field. No source code or production data changed. Actual production cause remains incomplete under AC1/T2.

## 対応案 — 2026-10-11

This earlier compatibility-only proposal is retained as history, not current execution instructions. See the migration-first update below.

### 1. 本番の原因と対象を確定する

東京3Rの失敗記録から、馬名、受信したJRA識別子、既存HorseId、失敗日時・試行・バッチを取得する。既存のプロフィール／収集対象の識別子と、最初の `HorseRegistered` の `RegisteredName`・`NormalizedName` を読み取り専用で照合する。京都1Rも同じ条件に該当するか個別に確認する。通知グループが同じという理由で他の対象を含めない。

ログイン更新後も最初の登録イベントを取得できない場合は、対象を限定した安全な読み取り経路を先に確保する。既存の手段で不可能なら、診断機能の追加は別の具体的設計・承認事項とする。新しい公開API、認証緩和、全DB出力は本案に含めない。

### 2. 旧IDを認識する読み取り互換性を追加する

対象は、既存の名前由来判定で認識できない、出典識別子未設定の馬に限定する。最初の登録イベントに非空の `NormalizedName` があり、次の条件を全て満たす場合だけ、既存の名前由来候補と同じ扱いにする。

1. 過去の固定namespaceと **当時と同じTrimのみ** のキー処理で生成したIDが、保存済みHorseIdに完全一致する。
2. 元の `NormalizedName` と現在の `RegisteredName` が、既存の同定用正規化で一致する。
3. プロフィール／収集対象の既知の出典が競合しない。後で読み込む収集対象メタデータも含めて、現在の優先順位・競合判定を維持する。
4. 既存の一意候補、他の同名馬、既知の生年月日、入力出典のチェックを全て通過する。

空値、登録イベント欠落、異なる名前、ID不一致は新しい証明として認めない。読み取れない履歴を推測で補完しない。現在の名前方式・旧JRA URL方式の既存認識を壊さない。

ID計算と証明判定はDBに依存しない小さな関数として、既存resolver内の単体テスト可能な形に分離する。新しい汎用基盤やresolver全体の再編はしない。保存された馬ID、過去イベント、出走参照を変更しないため、データ移行は不要。

### 3. 実際の読み込み経路までテストする

旧writerの式でIDを作り、異なる `RegisteredName`／`NormalizedName` を持つ最初の登録イベントをDBへ保存したfixtureから、`LoadHorsesAsync` と実際のresult-bulk HTTP処理を通す。成功時は元のHorseIdを利用し、馬の重複登録や過去イベントの変更がないことを確認する。

拒否側は、任意UUID-v5の同名馬、同名で異なるJRA識別子、出典競合、複数候補、正規化名不一致、登録イベント欠落／空値を確認する。競合で拒否された一括入力は、他の馬・出走・レース結果も含めてコアデータを書かない。分類関数だけのテストで完了扱いにしない。

関連テスト → API全体のReleaseテスト → 正式CIと同じformat/build/testの順に検証する。旧IDは現行helperから作らず、過去の式を独立に使うfixtureにして、テストが実装の前提を繰り返すだけにならないようにする。

### 4. 配備と限定的な復旧

既存のGitHub Actions経由で配備する。pause/drain、バックアップ、互換性確認、ヘルス確認を省略しない。ローカルAWS認証切れはGitHub側の配備経路そのものの障害とは区別する。

配備後は、原因・出典・旧IDの証明を確認した対象だけを再実行する。成功する置換ジョブ、元のHorseIdの維持、レース結果の保存、後続処理の進行を確認する。初回成功だけでなく、少なくとも後続のディスパッチと1件の終端結果まで観察し、所要時間に応じて観察期間を記録する。未確認の失敗が残る場合は無理に全体を再開しない。

### 5. 再発・ロールバック条件

証明に失敗した馬や別のエラーは、安全停止と元の失敗記録を維持して再調査する。別馬への紐付け、重複登録、未知の書き込み、即時再停止を確認した場合は再試行を止めてpauseし、原因を調べる。

互換性判定のコードだけを戻す場合も、現在のDBスキーマに対応した新しいビルドとして配備する。以前のスキーマ非対応バイナリへ戻さない。新旧IDの統合・削除やDBバックアップの上書き復元は本案の権限に含めない。既に保存された通常の結果を取り消す必要がある場合は、別途データ修復設計と承認が必要。

## Hypothesis and risk review

| ID | Claim and boundary | Evidence / falsifier | Result and disposition |
| --- | --- | --- | --- |
| H1 | Distinct historical NormalizedName input can leave a legitimate old name-derived UUID unrecognized. | Historical writer diff, current loader source, isolated actual-resolver probe. Old-formula loader/HTTP regression is required before completion. | Mechanism reproduced; candidate compatibility fix has a bounded basis. Not proof of Tokyo cause. |
| H2 | Tokyo3R and Kyoto1R were produced by that historical input. | Read each target's source bindings and first registration; recompute the exact historical ID. | Unverified; C1/C3 remain open. If false, revise the corrective design rather than relax guards. |
| H3 | A proved name-derived UUID alone establishes physical horse identity. | A same-name horse can have no stored source evidence; uniqueness in this DB does not imply global uniqueness. | False as a general guarantee. Retain all existing adoption guards and require target evidence before operational recovery. Residual legacy-policy risk must be explicitly reviewed before approval. |

Additional material concern C4 (AC2/AC4, T3/T4): this adds an exact historical origin proof, not an external proof of physical identity. The recommended boundary is to retain the existing recognized-name-ID adoption policy without weakening its uniqueness/source checks. Alternatives are to keep such targets blocked for manual source confirmation, or redesign all source-less legacy adoption; the latter is out of this narrow proposal. Risk and operational eligibility are not silently accepted; C2 stays open until target evidence and the policy boundary are reviewed.

Design/task-split review: Lead owns identity/data/security contract and acceptance; the existing Luna worker has exclusive planned ownership of the three implementation/test files and mechanical tests. No parallel source writes are needed. CI provides independent whole-change gates. Scope expansion into new diagnostics, data correction, or stop-policy changes returns to design review.

Concern review: data preservation, same-name ambiguity, inaccessible provenance, old-writer-shaped testing, deployment pause/backup and schema-compatible rollback are covered above. The proposal is saved as `Proposed`; C1-C3 and production evidence are not resolved by writing this plan. This turn creates the requested plan only and does not request approval for an unconfirmed production repair.

## Verification record

- 2026-10-11 authenticated site-only checkpoint: captured Tokyo target technical details, attempt history, execution batch, exact-name horse search/detail, and current collection pause banner. No retry, resume, data correction, credential entry, or production data write. C1 authentication/evidence gap closed; T2 remains incomplete for provenance. Documentation-only update; no source build/test or graph sync required.

- Migration-first continuation: fresh browser check still shows login; AWS STS reports expired session. Requested both renewals together. M1 is a read-only repository planning task; no migration/source changes or production mutations dispatched.

- 2026-10-11 proposal update: canonical execution audit passed; DDD validator at `.codex/skills/document-driven-development/scripts/validate_change_records.py` reported `issues=0`; `git diff --check` passed. The initially attempted repository-root DDD validator path was absent; used the existing skill-local tool instead. No implementation/build/test/deployment result is claimed by document validation.
- 2026-10-11: Target browser redirected to login. Displayed the login screen and requested user renewal. No credential entered.
- 2026-10-11: Fresh AWS STS read failed with expired-session status. No production mutation.
- 2026-10-11: Initial planning-record audit failed on task heading and required metric/evidence fields; corrected those fields and reconciled the read-only attempt audit. No source edit/readiness gate is claimed.
- Repository status: pre-existing user changes only in orchestration skill, its policy tests, `AGENTS.md`, and `docs/changes/20261003_luna-routing-fallback/`; preserve and exclude.

## Documentation updates

No non-change-record documentation changed: this remains investigation/design and changes no deployed contract. Relevant repository orchestration, DDD, production incident recovery, and read-only diagnostics instructions were inspected. [Whole-data migration proposal](whole-data-migration.md) and [source persistence inventory](persistence-inventory.md) are the current design supplements; historical compatibility-only proposals below do not govern execution. Architecture and prior subject-profile-job migration records were consulted; a task-repair workflow does not substitute for whole-domain canonicalization. Canonical documentation updates required by the eventual contract remain T3.

## Whole-data design expansion — 2026-10-11

The user asks for a principled migration of the complete dataset, not a local workaround. AC4 and M2 now explicitly cover the full storage/producer inventory. This is a request to investigate/design the expanded migration, not approval of unspecified history merges or production mutation.

Lead owns persistence/public-contract choices, security, operational cutover and acceptance; read-only source census is delegated to the existing Luna/high route. No ordinary application implementation is performed by Lead. The [method comparison and six-stage proposal](whole-data-migration.md) recommends consistent offline copies, typed transformation, complete comparison and stopped generation-aware cutover, subject to evidence. All valid/unaffected data remain unchanged; ambiguous or unknown records block the manifest rather than becoming guessed mappings.

Hypothesis review: a validated offline copy is safer to assess before replacement than incremental live rewrites, but sufficient disk, executable restore, external delivery fences and complete reference recipes are not yet demonstrated. A backup alone does not prove reversibility after new writes. These remain C5-C9/T2/T3, not approved premises. No whole-dataset production scan, backup, migration, reset, deployment or retry was executed.

Current next actions: finish/reconcile M2 source-defined inventory; obtain authorized bounded target data; freeze exact transformation/stream-collision/snapshot/delivery and cutover contracts in T3. If target access needs a new diagnostic capability, define its exact bounded read contract and seek approval rather than introducing it under general recovery authorization. Full implementation/recovery remain T4/T5.

Process verification note: active M2 audit initially failed because completed/unavailable telemetry fields were used for dispatch; setting entire sections to null also violated required object shapes. Router classified this as local audit-schema error, not a model escalation. Corrected active objects to null-valued fields and re-ran the same audit successfully. These were Lead audit corrections, no source implementation/test failures; completion metrics must not attribute them to the worker.

M2 checkpoint review: source-only inventory is Verified, not target migration feasibility. Lead independently reproduced 20 and 25 physical table mapping counts, verified no source diff, and used CodeGraph to confirm the race repair sidecar/event fingerprint gate (`RaceWriteCoordinator.ReadBarrierAsync:149–170`). Requested worker evidence closure for exact paths/physical mappings; final report covers those gaps. Requested model remains gpt-6-luna/high, observed model and usage are unavailable. No worker patch/test/build/production read exists to attribute. Existing tests are named but not rerun or treated as migration proof.

Expanded design verification: canonical execution audit passed; DDD changed-record validator reported issues=0; `git diff --check` passed (pre-existing policy LF/CRLF warning only). Current status/source diff preserves user changes. No build/test/formatter or CodeGraph sync applies to these documentation-only edits. Acceptance self-audit leaves AC1 target provenance and AC2-AC4 migration/deployment/recovery unfinished; this is a proposed design delivery, not an Implemented/Recovered claim.

## Admin command / UI design expansion — 2026-10-11

The user explicitly requires a migration command callable from the admin screen. AC5/U1 and [command/runner design](admin-migration-command.md) with [UI mock](mocks/data-migrations.md) capture this outcome; CLI-only/manual SQL is not sufficient delivery. Router separates read-only M3 from Lead-owned persistence/security/public/UX decisions. Command/engine, journal, runner, API adapters and UI remain unimplemented and require T3 readiness. No new runtime/deployment control permissions were installed or used.

UI skills applied: Fluent standard components, a dedicated page for multi-stage migration rather than a large dialog, server-paginated candidates, labelled confirmation and recoverable loading/empty/error/blocked states. bUnit and local browser test plans cover navigation, duplicate requests, stale preview, reconnect, narrow and keyboard behavior; none has run because there is no new UI code.

M3 checkpoint review: worker source inspection is accepted as repository-pattern evidence only. Independent Lead CodeGraph confirmed Settings authorization and server-side AdminApiClient key injection. Current compose/API image lacks a migration runner; in-memory status and startup MaintenanceMode cannot prove restart-safe execution. Proposed host one-shot runner/control journal needs concrete minimum authority, external delivery gate, interrupted cutover and temporary API/UI disconnect handling. C10 remains open; no approval request for whole migration/execution is made until these concerns are resolved. Existing public repair routes stay unchanged.

### Finite relevant route ledger (proposal; not implemented)

Existing relevant migration/preview surfaces discovered by M3: 5. Proposed new command routes: 6; separately proposed read-only diagnostic prerequisite: 1. Reconciliation: 5 unchanged + 7 additions = 12 entries below; no existing replacement/deletion in this slice. Command DTO wrappers, persistence, runner authority and final route naming remain T3 design gates. The diagnostic has its own bounded contract/approval; none of these entries is implemented by writing this ledger.

| Method/path | Disposition | Target / preserved invariant / consumer |
| --- | --- | --- |
| POST `/api/v2/admin/collection/migration-previews/race-detail` | Unchanged | Existing bounded collection preview; do not reuse as all-data apply. Existing Collection client. |
| POST `/api/v2/admin/collection/migrations/race-detail` | Unchanged | Existing bounded task migration; preserve history and auth. Existing Collection client. |
| POST `/api/v2/admin/collection/migration-previews/race-entry-owner-repair` | Unchanged | Existing owner repair preview; preserve scope. Existing Collection client/Settings. |
| GET `/api/v2/admin/collection/migrations/race-entry-owner-repair` | Unchanged | Existing task-derived progress; not generic migration status. Existing Collection client/Settings. |
| POST `/api/v2/admin/collection/migrations/race-entry-owner-repair` | Unchanged | Existing paused/no-running guard and task enqueue; not full DB cutover. Existing Collection client/Settings. |
| GET `/api/v2/admin/data-migrations` | Proposed addition | Allowlisted migration catalogue/availability only; authenticated server-side admin adapter/new page. |
| POST `/api/v2/admin/data-migrations/previews` | Proposed addition | Typed migration ID, idempotency; receipt operationId; no domain mutation, durable bounded diagnosis. New adapter/page. |
| GET `/api/v2/admin/data-migrations/operations/{operationId}` | Proposed addition | Durable phase/timestamps/counts/blockers/outcome; 404 for missing; receipt is not success. New adapter/detail page. |
| GET `/api/v2/admin/data-migrations/operations/{operationId}/items` | Proposed addition | Validated filter/pagination; no secret/raw DB export; complete summary separate from page. New adapter/Fluent grid. |
| POST `/api/v2/admin/data-migrations/executions` | Proposed addition | Fixed preview/plan fingerprint, target generation and confirmation/idempotency; server enforces all gates before independent runner executes. New adapter/confirmation page. |
| POST `/api/v2/admin/data-migrations/operations/{operationId}/resume` | Proposed addition | Resume only a stored, revalidated restart-safe checkpoint; reject unknown/unsafe state. No unconditional restore or new plan. New adapter/operation detail. |
| GET `/api/v2/admin/identity-diagnostics/horses/{horseId}` | Separately proposed diagnostic addition | Bounded exact-ID evidence only; protected server-side adapter/diagnostic page. No migration preview/apply; [diagnostic contract](../20261011_horse-identity-diagnostics/README.md). |

UI routes proposed: `/settings/data-migrations` and `/settings/data-migrations/operations/{operationId}`; existing `/settings` gains a link only. Server-side credential boundary remains; no browser credential exposure/arbitrary SQL/path/shell entry. Raw API key auth does not establish new operator roles or authority by inference.

Next action: settle bounded command/preview persistence and host-runner start/stop/journal/delivery contracts with exact tests and UI/client DTOs, then obtain design approval for those concrete changes. Target-data and history-transform gates still belong to T2/T3. Intentionally uncommitted files are design/evidence only; no application implementation, build/test, production command or data mutation occurred.

Admin design validation: execution audit passed, changed-record validator issues=0, diff whitespace check passed, and scoped source/deploy/tool diff was empty. M3 source evidence is Verified but AC5 remains Not started for implementation/verification. Requested Luna route is recorded; observed model/usage remains unavailable. Documentation-only changes need no production build/formatter or graph sync. Parent Proposed status and pending AC/task ledger remain reconciled; no completion/recovery claim.

### Diagnostic prerequisite continuation

M4 source plan reviewed in two passes (one evidence-closure clarification, no code retry). Lead independently confirmed registration fields, profile fields, exact JRA/Horse resource lookup and endpoint registration root with CodeGraph. Worker reported installed EventFlow metadata-provider probe; assembly version is not payload schema version and the probe was not rerun by Lead. This informs fail-closed diagnostic tests, not a proven migration mapping. Requested Luna/high route retained; observed model and usage unavailable.

[A separately bounded diagnostic proposal](../20261011_horse-identity-diagnostics/README.md) now freezes exact-ID read-only service/GET/UI, finite query/JSON/time/concurrency bounds, no-write and auth/HTTP/browser tests, canonical deployment and target evidence read. It is a prerequisite, not a replacement for AC1-AC5 or the required whole-data command. No permanent identity compatibility branch, runner privilege, API Docker socket, database write/reset or recovery action is introduced. `docs/20-admin-ui-design.md` gains only a clearly Proposed link through the child proposal.

Next operation: obtain explicit approval for that limited diagnostic contract, then Luna implementation/verification and canonical deployment; use authenticated site reads to close missing target fields in T2. Full dataset census, noncanonical IDs, complete historical sources/references, collisions, queue fence and cutover remain owned incomplete parent work. Diagnostic results alone do not approve migration. Parent Status remains Proposed; all production implementation and incident recovery remain uncompleted. Intentionally uncommitted files are these design/evidence records, the proposed canonical UI link and the earlier policy change; user policy/routing changes are preserved. No source edit, source tests/build, deployment or target mutation occurred.

Process note: Lead's active M4 audit initially used null usage availability, rejected by the validator. Corrected to unavailable with reason and reran successfully; this is an audit-record correction, not a worker retry or implementation result.

Design/task-split review for expanded scope: all-data preservation and cutover decisions remain Lead-owned; M2 source census is independently bounded and complete. T2 target provenance/census is still an acceptance blocker; T3 must settle serializer/fingerprint, collision, generation/cutover and delivery rules before T4 exact files/tests are runnable. No approval request for complete migration is made while these material concerns remain open. The concrete next operation is to define/obtain authorized bounded target reads using an existing path or reviewed diagnostic capability, not to require local AWS login for GitHub-owned execution. Intentionally uncommitted design/evidence files remain under this directory; policy/user-owned dirty files stay outside this change.

## Migration-first continuation — 2026-10-11

The user accepted the six-step migration-first outcome: verify Tokyo/Kyoto provenance, build old-to-canonical mapping and reference inventory, preview conflicts, backup/pause and migrate, unify runtime rules, deploy/retry and observe progress. This does not approve an unspecified event-history rewrite, ambiguous merge or DB reset. Status remains Proposed until missing target evidence and concrete migration/replay/collision contracts are resolved; do not substitute the earlier compatibility-only implementation.

M1 inspects EventFlow aggregate IDs/data, domain read models/references, profiles, repair redirects and collection resources/tasks/reservations. Existing tools may be reused only if they satisfy single canonical steady-state behavior and preserve domain integrity. Final event strategy and exact file scope depend on that inventory and representative target data, not assumptions. Unknown data remains an owned blocker, not an excluded follow-up.

Next operation: after login renewal read the actual Tokyo technical failure and provenance; after AWS renewal verify a bounded authorized read path (renewal does not prove SSH access). In parallel finish M1 inventory. Freeze migration preview/apply, collisions, cross-store interruption and replay tests before implementation. If preserving history requires a material change from the summarized plan, disclose that concrete choice rather than silently introducing permanent runtime mappings. Credentials stay out of transcripts/artifacts. DB reset is not dispatched.

### M1 bounded inventory and design boundary

- Event store: `EventEntity.AggregateId`, unique aggregate sequence, reference-bearing race event JSON, and `SnapshotEntity` identities/sequences. Source+target Horse streams can collide. Application schema has no demonstrated checksum contract, but EventFlow replay/snapshot guarantees remain to be verified; absence of a checksum column is not proof of safe rewriting.
- Domain projections/references: Horses, JRA profile SubjectId, horse race/weight histories; prediction contexts, results/comparisons and jockey history JSON; race-entry-derived IDs, tickets/marks/memos. Some projections are replayable, not all original input/user data is proven reconstructible.
- Collection store: resource IDs/unique keys and ResourcePk-linked states, requests, tasks, attempts, outbox, leases, artifacts, locations, failure notifications and selections; metadata such as requestedByHorseId. Resource key collisions need explicit rules and must not discard queued/inflight state.
- Existing horse repair creates persistent redirects/applied markers and suppresses old resource jobs; it neither rekeys Horse streams nor comprehensively rewrites references. It is not the requested migration. Existing redirect tests cannot close physical canonicalization criteria.
- Existing UI/API current-horse/profile/repair-preview reads do not expose the initial registration event. Next evidence is a consistent authorized backup/read-only clone of both stores, then bounded mapping, stream/snapshot, reference and queue-state inspection. Creating that backup must use an approved stopped/drained operational path; no backup or DB read was performed in M1.

Concrete candidate order after evidence/design gates: exact source-backed mapping manifest and complete typed reference ledger; choose transformation and collision policy; implement opt-in preview/apply (not automatic startup); verify no-write preview, idempotence, interruption recovery across stores, reference closure, stream hydration/replay and real handler path; remove prohibited compatibility runtime only after verified cutover; deploy/recover/observe. Rejecting canonical stream collisions rather than implementing merge is a bounded candidate, not yet the approved final contract.

Independent Lead review: CodeGraph pinned `ApplyHorseIdentityRepairEndpoint.cs` lines 67-105 confirms redirect creation, event-DB commit, then separate collection suppression. This substantiates both the existing-tool limitation and the cross-store interruption concern. No new runtime/deployment code, test, or data operation was performed. M1 completes repository planning evidence, not migration feasibility for the target or incident resolution. Execution audit and changed-record validator pass; actual target sample and C1-C3/C5-C6 keep T3/T4/T5 incomplete.

### Authenticated site evidence — 2026-10-11

Lead-owned read-only incident evidence (security/persistence risk boundary; no ordinary implementation delegated). The computer-use skill governed site reads; orchestration and DDD govern evidence reconciliation. The earlier expired-login observations above are historical, not the current site-access state.

- Tokyo target `Race/JRA/20261010:Tokyo:3/race-detail`: `DomainWriteRejected`, detail `HorseIdentityConflict: Horse=ナムラアラバイ; Source=jra-horse:2024100828`. Requested URL `https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1005202604030320261010/51`; final URL/HTTP unrecorded. No evidence of an incorrect URL is established by this failure.
- Attempt 27 started 2026-10-11 04:46:54 JST, ended 04:48:59 JST, duration 125.3 seconds. Attempt 26 was publication-pending on 2026-10-10 09:43:05 JST; the technical conflict is not 27 consecutive identity failures.
- Batch `02dd5b21-844e-4027-b92b-fd63b6941bf2`, envelope `cc6cee47-e06f-44c2-b623-122944d48eab`, queue message `8df8eaae-6f32-408a-9271-dc2792c9ab31`, Lambda request `fab8f5f2-5551-5fdb-bfab-96b6cd0850d5`. Batch UI shows 1/9 processed and one recorded failed task; the other eight outcomes are not established.
- Exact-name search returns one current horse: `horse-d198b3e2-771f-5dcb-94dc-a739e98b6119`, ナムラアラバイ, male, birth date unregistered, JRA public profile not fetched. History: 2026-09-27 Nakayama 3R, 2歳未勝利, 7th, 杉原誠人, 武市康男. One search result is not proof of physical identity or absence of all hidden source bindings.
- At UI refresh 06:38:26 JST, collection is globally paused with notification `39d52a64-ac29-462e-9820-3ebe42c131e6`, task `2fe7d2a6-458f-41e5-bd6b-e8c773c85be4`, and the same horse/source conflict. All three lanes have running=0; displayed waiting counts realtime 707, normal 4309, background 1189. This directly explains the current stopped state; do not attribute running=0 here to lane allocation or a busy-loop hypothesis.
- Horse detail edit/correction buttons produced no visible new screen on this session. No save/apply was attempted. These actions exposed no additional provenance; initial `HorseRegistered.RegisteredName`/`NormalizedName`, source-binding ledger, canonical-target streams and typed reference coverage remain unavailable.

Checkpoint acceptance: actual Tokyo failure and current stop reason are confirmed, but AC1 is not fully Verified and migration/recovery is not complete. H2 remains unverified: the old normalized-name-input mechanism demonstrated in a synthetic probe must not be asserted as Tokyo's proven cause. Next evidence must distinguish that mechanism from another source/name mismatch before selecting or applying a mapping. Preserve paired-data and collision review gates; no reset or blind retry.
