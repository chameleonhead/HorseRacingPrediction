# 停止後の全件検証による馬ID移行

- Status: Approved
- Change record schema: 2
- Owner: Lead
- Created: 2026-10-21
- Updated: 2026-10-23
- JRA site contract impact: None

## Context and scope

Approval2026-10-21: user「お願いします」explicitly accepts the preceding four-bullet summary of AC1–6/C1–4: pending-only UI save; authorized stop/backup/full validation beforewrite; preservation/idempotency/old compatibility; bounded downtime; tests/deploy/actual recovery. Later「全件でなくてもいいですが、決め打ちはやめてください」adds a generic-input constraint: no compiled business horse IDs/fixed three-target implementation. Remove production preset dependencies; operation may still use the previously approved3 mappings as user-selected input, never engine constants. Clarification pending only whether “not all” also requests narrower inspection; independently runnable genericadmission/UI work proceeds, existing complete reference-validation safeguards remain until explicitly changed.

## CURRENT

- User approved pending admission; ordinary execution routes to selectable Luna/high, Lead owns architecture/data/security/final acceptance.
- P1/P2 verified. P2 native API776passed/8skip/0failed, focused128pass, real browser1pass, exact formatter/build0; all15 Git-clean blobs independently match tested native candidate. Source checkpoint next; no runtime/writer edit inP2.
- No hardcoded business IDs/fixed3 default workflow; accept selected existing oldID/JRA input (existing max16 safety bound remains).
- Inspection-scope clarification pending; no relaxation of fingerprints/collision/unknown-data/preservation checks.
- Last production observation2026-10-21: cf0a37c7 CI/deploy success; actual3preview30sdeadline; globalpaused/Running0, no stagedoperation/migration/retry/resume byLead. Current live state must be refreshed at P5, not inferred from that observation.
- NEXT P2 component/target-review/focused/API/native formatter+build gates → checkpoint → serialized P3 offline binding/writer integration → P4 native+new900pending proof → P5 deploy/actualmigration/postchecks/separate retry/resume/lanes.
- Resume2026-10-23: orchestration/DDD/incident skills reloaded. Prior P2 worker stopped on account usage limit; user requested continuation after the dated reset window. Same Luna/high followup dispatch accepted, existing attributable diff preserved. Latest saved component r3:16passed/1failed; earlier focused runs116/125 then120/125 had no saved TRX. P2 remains unverified; no production mutation.
- Native SDK402 runner hrp-final-ci-runner-init-20261019-73dfd8a retained; create new candidate with git -c core.autocrlf=false archive HEAD + owned overlays, never copy generated runtime sidecars/bin/obj.
- Preserve unrelated dirtyAGENTS/skills/policytest/routing/efficiency changes; Lead owns records and commits, no worker push/prod operations.

Preapproval evidence2026-10-21: parent T27 cache-only change cf0a37c7498f1b25cdb727940d78425be38e8795 passed native and actualremote2035tests/8skip, app-ci39577374948/app-deploy39577374904 SUCCESS/all6checks. Actual3 approvedpairs still hit30s TypedReferences deadline: lastcompletedEventStore/Trainers,31742rows/4155706bytes/30081ms (completed-page lowerbounds, not fullvolume/hotspot). Therefore the same-contract optimization did not restore admission; no further blindpreview/timeoutincrease is planned. Approval above now authorizes the changed pending Stage contract; parent recovery remains unfinished through actual operational proof.

Parent: [admin-screen recovery](../20261017_admin-screen-recovery/README.md), F45/T26 and original3 approved hypothesis pairs. This proposal does not authorize Ecolotera, ambiguous same-name identities, bulk retries, resets, automatic resume or weaker data checks. Parent operational acceptance remains unfinished.

Actual03484dd0 cold/warm authenticated UI previews both stop at the30s HTTP deadline, during TypedReferences. Completed-page lower bounds:27167rows/1727548bytes and30533rows/3941480bytes. Last page does not identify the hotspot or total volume. Reader's existing whole-scan bounds are30min/1Mrows/4GiB. Native small fixtures are not production capacity evidence. No behavior-preserving optimization can guarantee unknown-volume full census in30s. No production data has been migrated.

## Hypothesis review

| Claim | Boundary and evidence | Falsifier/result | Disposition |
| --- | --- | --- | --- |
| HTTP deadline prevents current online admission | Fact: exact endpoint deadline branch and actual DeadlineExceeded metadata twice | Warm Lazy-cache falsifier still30010ms | Confirmed |
| Small immutable-options cache is sufficient to restore production admission | Native samephysicalFP/preservation equal and syntheticgain real, but production capacity unproved | Deployedcf0a37c7 actual3pairs stillDeadlineExceeded30081ms/TypedReferences | Falsified as sufficient recovery; retain harmless verified optimization, use explicit gate-relocation proposal |
| A specific table/JSON routine is the production hotspot | Inference only: sequential reader, lower-bound last pages | No per-phase/active-row timing or production volume | Not used as design premise |
| Existing offline lifecycle can preserve full checks | Source: supervisor owns drain/API exit/backup/fixed child; writer validates canonical inputs beforewrite | Tests must prove pending cannot reach write without binding and complete inspection | Reuse with explicit pending/bound contract; not permission to reuse standalone binding blindly |

## Proposed behavior and experience

Keep token-free completion inside the admin screen. Replace the requirement to finish a whole-database scan inside an online preview request with an explicit pending-validation request:

1. Online target review validates the selected existing old IDs, canonical JRA identities and explicit hypothesis acknowledgements using bounded point reads. Show selected pairs and clearly state **全データの検証は未実施**. Do not present this as a complete preview/fingerprint or offer automatic execution.
2. Save only the immutable selected manifest and request identity in the separate journal. Save/reload/reconnect does not start work. Request identity is distinct from canonical source evidence; never invent a fingerprint.
3. Operator explicitly chooses **収集を停止して全件検証・移行**. Use existing ownership, source-set/parent authorization, pause and900s drain/Running0, then completely stop the normal API. UI is unavailable temporarily, as in the existing approved design.
4. Under that ownership, validate the SQLite-consistent managed backup and filesystem capacity. Run the canonical full schema/root/typed/unknown-data/collision census with a supervised finite token and existing30min/1M/4GiB reader bounds. Atomically seal its real fingerprint to the exact immutable pending manifest before Applying/write authorization. Incomplete, conflict, timeout or cancellation cannot seal or write.
5. Reuse fixed CLI/writer preflight/page freshness checks and full post-verification. Keep their second read; avoiding it is out of scope. Unknown/collision/partial/crash remains blocked, not automatically replayed or resumed. Restart normal API paused only after verification succeeds; show result after reconnect. Retry affected jobs and resume collection remain separate authorized actions.

No new service, generic background queue, GitHub token, shell UI or preview-only second offline cycle. Existing online full-preview diagnostics can remain an optional read-only tool, not a prerequisite for pending-mode requests. Existing bound requests retain their original binding semantics; no destructive journal rewrite or reinterpretation of their hashes. The30min limit is for full inspection, not total downtime: existing drain, backup, writer and post-verification add their own bounded time. Do not promise a30min total outage or production throughput from synthetic fixtures.

### Wireframe (desktop and390px)

```text
選択した対応（最大16件、旧ID / JRA識別子）
全データの検証は未実施。収集停止後、書込前に実施します。
[内容を確認して依頼を保存]
保存済み: 全件検証待ち（保存・再接続では開始しません）
[収集を停止して全件検証・移行]
実行中は一時切断 → 再接続 → 結果 / 阻害理由
ジョブ再取得・収集再開は別操作
```

Keep existing stacked cards/details, accessible labels/focus, disabled duplicate actions, loading/error/empty states, manual status refresh and no secret/path/raw payload exposure. Long IDs wrap at390px. Explicitly distinguish pending, validated and completed; an absent/new/unknown field never means success.

## Public contract inventory

Paths below include the application's `/api` group prefix. Pending admission is an explicit versioned request kind, not a null fingerprint accepted as an ordinary legacy request. Shared producer/consumer DTOs and real HTTP/client tests must cover every changed shape, idempotency replay and read-back. Old bound requests remain bound; unknown kinds fail closed.

| Current operation | Disposition | Invariants / consumer proof |
| --- | --- | --- |
| POST /api/v2/admin/horse-id-migration-previews | Extend with explicit bounded target-review kind; existing full scan remains read-only | Auth/no-store/bounded body; target review cannot masquerade as full evidence; shared HTTP/client |
| GET /api/v2/admin/horse-id-migration-schema-diagnostics | Unchanged |30s safe metadata, no business rows |
| POST /api/v2/admin/horse-id-migration-operations | Extend explicit pending kind | Manifest/ack validation, immutable identity, exactly-once save, no automatic run; no fabricated fingerprint |
| POST /api/v2/admin/horse-id-migration-operations/{operationId}/execute | Accept explicitly pending operation via same supervisor only | Auth/CSRF/ownership/drain; no direct pending writer route |
| GET /api/v2/admin/horse-id-migration-operations/{operationId} | Add explicit pending/bound evidence status | Read-only, no capability/secret leakage; unknown blocked |
| GET /api/v2/admin/horse-id-migration-requests/{requestKey} | Preserve identity replay and new status | Same manifest duplicate returns same request; different manifest409 |
| POST /api/v2/admin/horse-id-migration-operations/{operationId}/verify | Unchanged | Existing succeeded-only maintenance re-verification |
| GET /settings/horse-id-migration | Update normal UI; maintenance result page unchanged | No auto action on reconnect; shared status semantics |
| POST /settings/horse-id-migration/{operationId}/verify | Unchanged | Existing CSRF/succeeded-only guard |

Inventory9:6 extended (including UI GET),3 unchanged; no route deleted or new route needed. Selection uses existing candidate/identity endpoints unchanged and outside this admission-contract change.

## Concern and agreement ledger

Lead recommends the pending-manifest route over extending HTTP timeouts (availability burden and unknown scan duration), an API async job (extra lifecycle/polling/state), a second preview-only offline cycle (two outages/binding machinery), or skipping validation (unacceptable). Micro-optimizations may help but are not this proposal's recovery premise.

| ID | Evidence / impact | Recommended disposition; alternative; residual risk | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- |
| C1 | Online full-data evidence will no longer precede saving/explicit Execute | Saving is pending only; full checks/real evidence still mandatory after API exit and beforewrite. Alternative retain blocked online gate; review shows pending clearly | AC1/2/3; P1/P2/P3; pending directwriter reject | Agree with this explicit gate relocation | Approved2026-10-21 | Resolved in design |
| C2 | Pending/bound identity, duplicate requests and crashes can create a binding gap | Stable immutable manifest request identity, separate exactly-once sealed canonical evidence; ownership/gate hashes never silently mutate meaning. Old bound records preserve semantics; durable migration only additive/journal-compatible. Reject manifest/source/backup mismatch, concurrent bind and partial binding; no blind standalone method reuse | AC2/3/4; P2/P3; crash/duplicate/mixed-source tests | Agree; do not bypass fingerprint | Approved2026-10-21 | Resolved in design |
| C3 | Site downtime and unknown data/volume may still prevent migration | Retain900s drain and finite fullscan budgets; backup beforewrite; fail closed with useful status. No guarantee all unknown data is migratable; do not force/reset. Retry/resume separate | AC3/4/6; P3/P5; large/unknown/cancel/partial tests | Agree with preserved stop conditions | Approved2026-10-21 | Resolved in design |
| C4 | Changed cross-process path invalidates happy-path coverage of old lifecycle alone | One new real pending-mode900s lifecycle plus bounded crash/kill/ownership tests, whole CI and actual3pair receipts. Reuse old unchanged-path tests without claiming they prove new pending path | AC4/5/6; P4/P5 | Agree; this adds necessary verification time | Approved2026-10-21 | Resolved in design |

No unresolved agent objection. Production throughput, total volume and extra Ecolotera identity remain unknown, not silently approved assumptions. Partial migration never clears the maintenance gate or resumes collection. Restore/reset after partial failure requires exact-target evidence and fresh authority.

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Logged-in operator can review exact approved mappings and save pending request without full online census; UI clearly says unvalidated; no write or auto run | P1,P2,P4 | Real HTTP/client+bUnit+390px/keyboard/loading/error/duplicate; malformed/ambiguous/unknown blocked | Connected |
| AC2 | Pending identity is immutable/idempotent and canonical evidence is sealed exactly once to same manifest/source set; no fabricated fingerprint or direct unbound write; old bound journal records remain usable | P1,P2,P3,P4 | SQL read-back/roundtrip, same/different key, concurrent/crash binding, old fixture migration, caller contract tests | Connected |
| AC3 | Existing authorized supervisor stops/drains normal API, validates backup/capacity, completes all configured-data census before firstwrite, retains collisions/unknown data/cancel/row/byte/time limits and writer freshness | P1,P3,P4 | Independent beforewrite invariant/counterexamples and full all-store tests, held API/failed backup cannot apply | Not started |
| AC4 | Full data preserved except approved identity changes; restore evidence retained; partial/kill/timeouts remain blocked/no replay/no auto resume; successful restart paused | P1,P2,P3,P4 | Typed/global hashes, backup/postcheck/replay tests, real lifecycle/crash controls | Connected |
| AC5 | Final source matches actual tested input, exact formatter/build/fullCI and original deploy gates pass | P4,P5 | Native complete commands +matching-SHA CI/deploy/annotations; no skipped mandatory branch | Not started |
| AC6 | Actual original3 approved pairs migrate successfully with backup and postchecks; selected replacement jobs succeed, then separate resume demonstrates three-lane progress | P5 | Actual UI GUID/status/receipt, failure history and lane timestamps; no forced fourthpair/reset. An external blocker leaves this AC and recovery unfinished | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| P1 | Source-informed plan and exact task split; Lead freezes architecture/security/data contract | pending_planner | Luna/high — readonly bounded planning, Lead retains material decisions | Approval | Read-only migration contracts, journal, supervisor, endpoints, admin page and related tests | CodeGraph/source/caller/consumer and test tracing; exact scope/commands; canonical audit/DDD | Genericengine/UIpreset source confirmed; root point-read seam/sharedDTO/source/backup evidence traced. Lead corrected pre-intent seal deadlock; ordered contract frozen below | Verified | Worker — bounded repository planning; Lead architecture/data/security acceptance | P1-A1 | unavailable; retries 0; corrections 1; reviews 2 |
| P2 | Pending DTO/journal/root-review/endpoint/UI admission | pending_admission_executor | Luna/high — ordinary scoped implementation | P1 | Exact P2 source/test list below; P3 serialized after P2 | Focused transport/journal/engine/UI tests then handoff hashes | Native API776passed/8skip/0fail; focused128pass; real browser1pass; exact formatter/build0; Lead source/TRX/blob checks, patch9a50d75d | Verified | Worker — frozen bounded implementation/verification after approval | P2-A1 | unavailable; retries 10; corrections 1; reviews 2 |
| P3 | Offline full validation and existing supervisor/writer integration | pending_planner (P3 executor after P2) | Luna/high — reuse source-informed planner for frozen lifecycle implementation | P2 | Exact six-source/seven-test P3 list below; serialized after P2 checkpoint | Beforewrite/backup/oldformat/kill/timeout/partial tests | Read-only P3 caller/capability plan returned; Lead freezes BackedUp census/separate seal/central writer guard, no new capability. Implementation awaits P2 checkpoint | Dependent | Worker — frozen bounded implementation/verification after approval | none | unavailable; retries 0; corrections 0; reviews 0 |
| P4 | Independent integrated counterexamples/native gates | lifecycle verifier | Luna/high — mechanical verification and local corrections | P3 | Relevant tests plus disposable isolated environment only; source corrections return exclusive executor | Exact CI format/build/test, real pending900s plus crash, full consumer roundtrip | Waiting integrated P3 source; no runtime/model/usage inferred | Dependent | Worker — frozen bounded implementation/verification after approval | none | unavailable; retries 0; corrections 0; reviews 0 |
| P5 | Deploy, execute operator-selected approved mappings, verify recovery | Lead | Lead — production/data/security/final acceptance | P4 | Canonical GitHub deploy/admin UI and governing records only | Actual matching-SHA gates/backup/apply/postchecks/selectedretry/separateresume/3lanes | Waiting P4; original3 are operationinput only, never productionconstants. AWS login not required | Dependent | Lead — architecture/data/security/final acceptance | none | unavailable; retries 0; corrections 0; reviews 0 |

Delegation uses selectable Luna/high; requested and observed model/usage recorded separately at dispatch. No cost or speed claim. Exact test commands and file ownership freeze in preimplementation plan; no coding until approval/canonical audit. T25/T27 source and deployment are now verified; no remaining independent approved source slice bypasses this approval boundary.

## Review gates / documentation

Preimplementation2026-10-21: main read orchestration/DDD/incident and FluentUI/form/page/UI-testing skills fully; audit and format references loaded. P1 source CodeGraph confirms core manifest is generic(max16), only RazorPresets fix3 business IDs. Existing search Query is name-only; exact ID must set existing HorseId filter, no newroute/editor. Root-only seam is StoreReaders.InspectRootsAsync with selected-ID SQL IN; factor it separately, never mislabel fullpreview. Main independently reads immutableV1hash/CreateExecutionIntent and managedbackup validation. Correct ordering: pending save → explicit intent(manifestidentity, not writecapability) → drain/APIexit → managedbackup → fullcensus/atomicseal → Applying/writer → postchecks/pausedrestart. Requiring seal beforeintent would deadlock; rejected with one planning correction. Use existing backup SourceSetHash/ManifestHash/member inventory, not a new receipt format. PendingV2 immutablehash stays unchanged acrossseal; oldBoundV1hash never reinterpreted. Seal/evidence required beforeApplying/writer, not beforemanualintent. Missingkind defaults legacyBoundV1 only; explicitunknownkind/review contradictions blocked. PendingStage must reject suppliedfullFP; no fake fingerprint. Canonical source evidence binds exactlyonce to same manifest/source/validatedbackup; same/different key andkind tested. Old bound reads keep old requirements. New pending fullreference path unchanged until clarified; do not narrow guards from permissive “not all” alone.

P2 edit release: selectable6Luna/high spawnaccepted; worker read requiredskills/record, traced endpoints/readers/engine/journal/client/Razor/tests and existingDI(no Programeditneeded), returned ordered contract→purepolicy→journal→pointreader→transport→UI→tests plan. Lead accepts within frozencontract/exactscope. Canonicalaudit0 immediatelyprecedes editpermission. Pending save→freshjournal readback/startupReady, samevalidmanifest acrossrequestkinds cannot reinterpret/coalesce, enumordinals stable, manualintentnotwriteauthority and arbitrarynonpreset exactID/name search are requiredcounterexamples. Scope is9source+6testpaths (15total), not15sourcefiles; new ExecutionPolicyTests is the only optionalnewpath. Sourcewriter/Runtime/Program remain excluded. Inspection-scope question is optional refinement; P2 changes no fullreference guard and is independently runnable.

P2 exact write scope: `src/HorseRacingPrediction.Contracts/Identity/HorseIdentityMigrationOperationContracts.cs`; `src/HorseRacingPrediction.Contracts/Identity/HorseIdentityMigrationPreview.cs`; `src/HorseRacingPrediction.Api/IdentityMigration/{HorseIdentityMigrationJournal,HorseIdentityMigrationExecutionPolicy,TypedHorseIdentityStoreReaders,TypedHorseIdentityStoreEngine}.cs`; `src/HorseRacingPrediction.Api/Endpoints/IdentityMigration/HorseIdentityMigrationEndpoints.cs`; `src/HorseRacingPrediction.Api/Web/ApiBrowsing/AdminApiClient.HorseIdentityMigration.cs`; `src/HorseRacingPrediction.Api/Web/Components/Pages/HorseIdentityMigrationAdmin.razor`; `tests/HorseRacingPrediction.Api.Tests/{HorseIdentityMigrationJournalTests,HorseIdentityMigrationEndpointsTests,HorseIdentityMigrationClientTests,HorseIdentityMigrationAdminComponentTests,HorseIdentityMigrationStoreEngineTests,HorseIdentityMigrationExecutionPolicyTests}.cs` (last may be new). No other source/test files, no records/commits/prod. P2 uses explicit versioned target review querykind on existing rawmanifest preview POST, distinct target-review DTO without fullFP/evidence; legacy fullpreview default retained. Remove Presets/RestorePreset, initializeempty with search instruction, reuse existing add/ack flow, one primary action perstate and optionalfullpreview kept secondary. Requested6Luna/high; no observedmodel/usage inferred. Worker first returns concise own source/test/DI plan, then Lead release/canonicalaudit0 precedes edit. P2 tests do not claim new900/lifecycle completion; P3/P4ownintegration. Phase enums/journal persistedvalues must not be reordered. No inventedreceipt/service/queue/token/forcedmapping/timeoutincrease.

P2 focused command: `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --no-restore --configuration Release --filter "FullyQualifiedName~HorseIdentityMigrationJournalTests|FullyQualifiedName~HorseIdentityMigrationExecutionPolicyTests|FullyQualifiedName~HorseIdentityMigrationEndpointsTests|FullyQualifiedName~HorseIdentityMigrationClientTests|FullyQualifiedName~HorseIdentityMigrationAdminComponentTests|FullyQualifiedName~HorseIdentityMigrationStoreEngineTests"`. Expect allpass; include real HTTP+AdminApiClient review/save/status/replay and arbitrary nonpreset oldID name/IDsearch; unknownkind, changedmanifest/kind withsamekey, suppliedFPforpending, unsupportedrelateddata not falselyclaimedvalidated, rootmissing/destinationcollision, duplicateclick/empty/loading/error/390px/keyboard semantics. Broader API nonExternal regression plus exact formatter and Releasebuild beforehandoff; fullnativewholeCI once finalP3/P4integration, not peredit. P3/P4 require pendingwriterbeforeseal/backup/sourcechanged/cancel/concurrentseal/partialbind/crash/oldbound fixtures and newreal900 path. Preservefailedreceipts and freshtrackednativecandidate; no copying runtime sidecars or repeatedunchangedcommands. Scope expansion/security/hash contract contradiction returnsLead before affectededit.

Design/task split and concern review: Lead/source plus read-only T26 evidence; pending is not full preview; fingerprint and binding safeguards explicitly retained at offline boundary. No new permanent service or speculative optimization. Approval is for all six ACs and C1–C4 recommended dispositions only.

P3 source-backed refinement2026-10-23 (Lead architecture decision; implementation remains Dependent on P2): reuse the supervisor control lease and installed maintenance gates after API exit for read-only full census; do not issue Applying authorization early or invent a capability. At BackedUp, use the existing minimal EventFlow services plus configured reader options, bounded full inspection, and validated managed-backup receipt. Atomically persist a separate canonical seal keyed by operation and unchanged PendingV2 manifest identity, real fingerprint, source-set hash and existing backup ManifestHash. Keep pending admission readable after sealing and strict BoundV1 getter/hash unchanged; use a kind-aware internal executable lookup rather than reinterpreting PendingV2 as BoundV1. Require the seal before Applying and centrally before writer lease/status Running/physical writes, comparing imported authorization's source set. BoundV1 retains existing fingerprint checks. Preparation must accept pending manifest identity for intent; blocked/read-only status must preserve its kind after seal. Existing phases suffice; no new service, endpoint, receipt or test hook. Pending standalone binding/execution stays rejected.

P3 planned exact source ownership, released only after P2 handoff: `src/HorseRacingPrediction.Api/IdentityMigration/{HorseIdentityMigrationJournal,HorseIdentityMigrationPreparationService,HorseIdentityMigrationSupervisor,HorseIdentityMigrationRuntimeMode,TypedHorseIdentityStoreEngine,TypedHorseIdentityStoreWriter}.cs`. Matching test scope: `tests/HorseRacingPrediction.Api.Tests/{HorseIdentityMigrationJournalTests,HorseIdentityMigrationPreparationTests,HorseIdentityMigrationSupervisorTests,HorseIdentityMigrationRuntimeModeTests,HorseIdentityMigrationStoreWriterTests,HorseIdentityMigrationManagedBackupTests,HorseIdentityMigrationPostVerificationTests}.cs`. Missing/conflicting/partial/concurrent seal, backup/source drift, cancellation, central prewrite rejection, pending readback and legacy bound behavior are required. No parallel shared-file edits with P2. P4 owns real Linux PID1 pending900/crash proof and final full native CI; source unit tests alone do not close lifecycle acceptance.

Preimplementation planning is recorded above; P2 source release follows its own plan/audit gate. Checkpoint and final implementation gates not yet complete. Parent and original hypothesis records remain unfinished through deployment/operational proof.

Documentation updates: `docs/20-admin-ui-design.md` now links the deployed recovery contract and labels this delta Approved/in implementation, not deployed. It distinguishes pending save from validation/write and removes fixedbusinessID/preset assumptions. Inspected recovery/operator materials found no separate canonical migration runbook. Reconcile parent scope and operator instructions on implementation/actual deployment, never infer operation completion from code.

## Verification record

P2 Lead independent checks2026-10-23: `codegraph sync .` exit0/up-to-date; re-query `InspectTargetReviewAsync`, `StagePendingWithRequestKeyAsync` and client entry points traces point-review to endpoints and real HTTP tests. Literal search across `src`, `scripts`, `.github` for all original3 old/new IDs and names returned no production hits (rg exit1/no matches), excluding only generated bin/obj. Focused final-r12 TRX128/128 independently parsed; native candidate gates still pending, so no source checkpoint or operational-completion claim yet.

P2 scope refinement2026-10-23: native API regression exposed browser helper `HasDisabledPrerenderedCandidateCheckbox` requiring the removed preset name. Approved genericempty behavior has no candidate checkbox before operator search/add; this is a stale test contract, not permission to weaken hydration. Add exactly `tests/HorseRacingPrediction.Api.Tests/HorseIdentityMigrationAdminBrowserTests.cs` to the existing P2 write scope (9source+7allowedtests,16maximum; optional policy test remains optional). Same owner; no runtime/source expansion. Preserve empty prerender/no auto action, then real arbitrary fixture search→add→ack→target review→pending save, mobile keyboard/reconnect/no autoexecute invariants. Existing legacy fullpreview keeps separate regression coverage. Canonical audit must pass before this added-file edit; focused native browser and affected final formatter/build/API gates must pass afterward.

P2 checkpoint review2026-10-23: Lead accepts AC1/2/4 admission slice only, not overall recovery. Frozen candidate `/workspace/candidate-p2-a1-final-20261023` in SDK402 runner uses tracked HEAD101fa0c9 +15owned files, no generated sidecars. Exact formatter exit0(session76357), Releasebuild0/0warnings/errors; native API final-r5 TRX776passed/8skipped/0failed (SHA256833681AA67605CF63452A82E3E774F6EF005F65F6C67D7D1AE80E04D8C5AAC50), browser-r4 1pass and focused-r12 128pass. Lead independently parsed final receipts and compared all15 host/native Git-clean blobs; BrowserTests raw differs only CRLF/LF. Source patch-id9a50d75d3ba76f9ad7def3faa2ac41004fc29e37. Record metrics10 documented failed-run retries (initial focused2/component1/rootfixture1/Verifiedcopy2/focusedassert1/nativeAPI1/browser2); absent earlier TRX not reconstructed. One Lead phase-label correction; two review groups, no model/usage inference or efficiency claim. DDD three-record validator0 and canonical audit0. Source checkpoint may commit; P3/P4/P5 remain unfinished and no push/deploy/operation yet.

| ID | Task | Failure | Required closure | Severity | State |
| --- | --- | --- | --- | --- | --- |
| F1 | P2 | Worker first focused125tests reports116Passed/9Failed, all component cases still assume preset setup/old primary label/global request totals; two earlier localcompile corrections alreadyclosed by successfulbuild | Closed2026-10-23: original six-class filter final-r12 TRX independently read by Lead:128executed/128passed/0failed, including phase-aware Verified label. Component oracle now checks FluentButton disabled state rather than matching explanatory prose. First two failures had no saved TRX (not reconstructed); r3/r5 and subsequent receipts retained. Native/full lifecycle remain separate gates | Material | Verified |
| F2 | P2 | Native API regression browser test line73 expects disabled preset checkbox; helper requires a removed business horse name | Closed: browser-r4 1passed, final API-r5 776passed/8skip/0fail. Generic empty prerender/search-add/manualstatus/mobile keyboard/noauto preserved; only added browser test path changed | Material | Verified |

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Connected | P2 admission/UI/journal implemented in attributable uncommitted diff; P3 offline sealing/writer integration remains dependent |
| Verification | Connected | P2 focused final-r12:128passed/0failed independently confirmed; native API/formatter/build, P3/P4 lifecycle and actual recovery still required |
| Deployment/operation | Not started | Parent live recovery incomplete; collection paused |
