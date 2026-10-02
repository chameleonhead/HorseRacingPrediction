# Remaining collection error triage

- Status: Implemented
- Change record schema: 2
- Owner: Collection operations
- Created: 2026-10-02
- Updated: 2026-10-02

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Complete | Unsafe pedigree expansion is removed and official source-bound namesakes remain distinct without weakening source-less ambiguity guards. |
| Verification | Complete locally | Focused and full suites, build, formatting, diff hygiene, and live parser probes pass. |
| Deployment/operation | Complete | CI and deployment passed; the pipeline is running, all six priority-1000 recovery tasks succeeded, and the production dashboard reports zero failures. |

## Context

The production collection dashboard observed at 2026-10-02 11:39 JST showed 20 actionable resources
outside the already recovered `20201206:Chukyo:11` path:

| Error group | Count | Latest observed | Known sample |
| --- | ---: | --- | --- |
| `horse-profile / SubjectNotIdentified` | 12 | 05:49 JST | Public search returned no candidate for `マルカクラック`. |
| `horse-profile / HorseIdentityEvidenceRequired` | 6 | 10:34 JST | `エピファネイア`; profile persisted, reference discovery incomplete, race history not started. |
| `race-detail / DomainWriteRejected` | 2 | 10:36 JST | `20260531:Kyoto:6` (`HorseIdentityConflict`) and `20160417:Nakayama:11` (`HorseIdentityEvidenceRequired`). |

The pipeline remained running. Eight separately requested 2026 race recoveries were in automatic
backoff after upstream access-limit/transient-server responses and are not counted as a new deterministic
error group in this record.

## Goals

- Establish a fact-based cause boundary for each current error group.
- Prevent parent horse-profile completion from being lost solely because an optional pedigree reference
  lacks enough identity evidence.
- Preserve the fail-closed rule for same-name horses and race-result writes.
- Define bounded recovery steps that do not bulk-retry deterministic failures.

## Non-goals

- Do not relax Horse identity uniqueness or same-name conflict checks.
- Do not dismiss or merge a resource from group-level evidence alone.
- Do not retry the two race write failures until their current source identities and assignment evidence
  have been read back.
- Do not treat JRA access limiting as proof of an application defect.

## Evidence and cause classification

### 1. `HorseIdentityEvidenceRequired` during horse-profile reference discovery

This path is structurally identified. `JraSubjectProfileCollectionHandler` persists the parent profile,
then `DiscoverHorseReferencesAsync` resolves trainer, sire, and dam references. Sire and dam call
`UpsertHorseAsync(name, null, null, null)`, so only the name is sent to the Horse identity resolver.
If an official/source-bound same-name Horse already exists, `CollectionIdentityResolver` correctly
rejects the name-only request with `HorseIdentityEvidenceRequired`. The handler catches that exception
around the whole reference-discovery step and fails the parent task after its profile was already saved;
the persisted message explicitly reports `ProfilePersisted=True; ReferenceDiscovery=Incomplete;
RaceHistory=NotStarted`.

The application defect is not the fail-closed resolver. It is coupling optional child-reference
discovery and subsequent race-history discovery to the terminal success of the already persisted parent
profile, without a separately retryable reference work item carrying source evidence.

### 2. `SubjectNotIdentified / NoCandidate`

The handler maps zero/multiple/mismatched public-search candidates to isolated permanent
`SubjectNotIdentified`. Existing recovery deliberately refuses unconditional retries at the same
definition revision. The group-level sample proves only that public search returned no candidate; it
does not prove whether each of the 12 names is an invalid extracted reference, a JRA directory omission,
a normalization defect, or a stale pre-fix task. The repository already provides a repair preview that
can classify a safe merge, validated URL retry, obsolete reference dismissal, or blocked case.

### 3. `DomainWriteRejected`

`JraRaceCollectionHandlers` uses this outer code whenever the domain result API returns item errors.
Identity-only failures are isolated; other write failures can stop the pipeline. The two current race
resources expose inner codes `HorseIdentityConflict` and `HorseIdentityEvidenceRequired`, but the
group view does not prove whether they are stale attempts predating the JRA stable-number fix or distinct
persisted conflicts. They require individual resource/attempt/batch and current assignment read-back
before any retry or repair decision.

## Hypothesis ledger

| ID | Claim | Fact / inference boundary | Falsification and current result | Disposition |
| --- | --- | --- | --- | --- |
| H1 | The six evidence failures are caused by name-only sire/dam resolution after parent persistence. | Code path and persisted stage message prove the boundary; the exact reference role for every item is not yet read back. | Read all six resource attempt messages and subject names; verify they originate inside `DiscoverHorseReferencesAsync`. One sample (`エピファネイア`) and the exact stage text match. | Supported; per-item inventory required. |
| H2 | All 12 no-candidate failures are malformed pedigree references. | Only one public-search sample is known; legitimate historical horses can also be absent from the current public directory. | Read task metadata, page-identification kind, candidate evidence, ancestry provenance, and repair preview for all 12. | Open decision; do not bulk-dismiss or retry. |
| H3 | Both race write failures are fixed by the deployed stable JRA horse identity normalization. | The deployment fixed route-family aliases, but these two current attempts have not been correlated with their source identities. | Read each resource history and execution batch, then compare stored/incoming stable horse numbers before a controlled retry. | Open decision; no mutation. |

## Proposed corrective design

1. Stop creating Horse aggregates and `horse-profile` tasks from sire/dam display text. The canonical
   parent profile already stores sire/dam text; the JRA profile surface does not provide source-bound
   parent links, and name-only expansion is unsafe for foreign and same-name horses. Trainer and race-history
   discovery remain enabled.
2. Complete the parent horse-profile task after profile persistence and continue race-history discovery;
   removed pedigree expansion must create neither a child task nor an identity API call.
3. When a new official JRA Horse identity has only other source-bound same-name Horses, create/select its
   deterministic official identity. Same-name source-bound Horses are legitimate namesakes. If any
   source-less name-derived candidate also exists, remain fail-closed because its ownership is ambiguous.
4. Close the 12 existing name-only pedigree tasks as obsolete generated references after deployment.
   Retry the 11 parent profile tasks under the new revision and verify profile plus race-history completion.
5. Retry the three race failures once after deployment. The 2016 live parser probe proved all 18 result
   rows carry official Horse identities and its failure predates the deployed route normalizer. The two
   2026 conflicts are handled by the official namesake rule; same-race source changes still fail atomically
   through effective horse-number collision validation.

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | Treating pedigree names as authoritative Horse identity can merge namesakes. | Silent cross-horse corruption. | Keep source-bound resolution and remove name-only pedigree upsert. | AC2/T2; namesake counterexample | Agree with removal of name-only fallback | Approved by user instruction to complete recovery | Resolved in design |
| C2 | Removing pedigree child collection could hide missing pedigree data. | Parent Horse enrichment would be incomplete if sire/dam text were not already canonical profile data. | Preserve sire/dam text in the parent profile; remove only unsafe recursive Horse collection. | AC1/AC3/T1-T3 | Agree; source inspection proves fields are persisted before discovery | Approved by user instruction to complete recovery | Resolved in design |
| C3 | Twelve no-candidate items may have mixed causes. | Bulk dismissal or retry can lose evidence or create loops. | Inventory proved all 12 are pedigree names, including foreign sires and two ambiguous namesakes; close only these exact generated resources after deployment. | AC4/T4 | Agree with exact-membership closure only | Approved by user instruction to complete recovery | Resolved in design |
| C4 | Three race errors may be stale or represent real duplicate identities. | Blind retry may repeat deterministic rejection; blind repair may rewrite correct assignments. | Live probes prove official identities on every row; deploy the namesake rule, retry exactly three, and retain atomic same-race collision rejection. | AC5/T5 | Agree with controlled retry after regression coverage | Approved by user instruction to complete recovery | Resolved in design |
| C5 | Extra JRA discovery would increase requests during existing access limiting. | More 429/transient responses and queue pressure. | Remove pedigree discovery calls entirely; retain existing bounded trainer/race discovery and verify request counts. | AC6/T2/T3 | Agree; the selected design reduces requests | Approved by user instruction to complete recovery | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | A persisted parent horse profile completes trainer and race-history discovery without creating sire/dam Horse tasks; sire/dam text remains persisted in the parent profile. | T1-T3 | production-shaped handler integration test | Verified |
| AC2 | No Horse is created, selected, or merged from a pedigree name alone when official/source-bound same-name records exist or candidates are ambiguous. | T2 | resolver/handler counterexamples for namesake, ambiguity, and conflicting birth evidence | Verified |
| AC3 | Official source-bound namesakes are distinct Horses, while a source-less same-name candidate or a same-race source change remains fail-closed and atomic. | T1-T3 | resolver and collected-race counterexamples | Verified |
| AC4 | The exact 12 generated pedigree resources are closed without retry and no unrelated failure is changed. | T4 | exact production membership and post-operation reconciliation | Verified |
| AC5 | All three race write failures are retried after deployment and persist result/lifecycle evidence without identity or horse-number errors. | T5 | live parser probes plus controlled production verification | Verified |
| AC6 | The correction does not materially amplify JRA requests; concurrency, deduplication, 429 handling, and pipeline safety-stop behavior remain verified. | T2-T3 | request-count, retry classification, dispatcher, and production observation | Verified |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Freeze removal of unsafe pedigree expansion and official-namesake behavior. | Lead planner | High capability; identity/public-contract decision | T4,T5 | change record and contract design only | source and production inventory | approved decisions and counterexamples | Verified | Lead - public contract and identity decision | none | unavailable; retries 0; corrections 0; reviews 2 |
| T2 | Remove unsafe pedigree expansion and allow distinct official source-bound namesakes without relaxing ambiguous legacy guards. | Cheap executor | gpt-6-luna high; bounded implementation after contract freeze | T1 | Collector, API identity integration, focused tests | focused handler/resolver/collected-race tests | focused API 13 passed; focused Collector 45 passed | Verified | Cheap executor - frozen, independently testable implementation | requested model not externally observable; patch attributable to this task | unavailable; retries 0; corrections 0; reviews 1 |
| T3 | Verify integrated persistence, request volume, idempotency, and existing operator behavior. | Cheap verifier | gpt-6-luna high; mechanical verification after T2 | T1,T2 | tests and verification only | full suites, solution build, formatter | API 407 passed/1 skipped; Collector 411 passed; build and format clean | Verified | Cheap verifier - regression gates | requested model not externally observable; no separate patch | unavailable; retries 0; corrections 0; reviews 1 |
| T4 | Inventory and classify the 12 no-candidate resources read-only. | Cheap investigator | gpt-6-luna high; read-only evidence gathering | authenticated session | production GET/repair preview only | exact 12-item reconciliation | all 12 are name-only pedigree expansions; 10 no-candidate and 2 ambiguous namesakes | Verified | Cheap investigator - bounded read-only inventory | none | unavailable; retries 0; corrections 0; reviews 1 |
| T5 | Diagnose the three race write failures and choose controlled recovery per item. | Cheap investigator, Lead accepts recovery | gpt-6-luna high; read-only first, Lead owns mutation decision | authenticated session | production GET and isolated live probe | three complete result identity inventories | all official rows carry normalized Horse identity; timestamps and inner codes recorded | Verified | Cheap investigator for evidence; Lead for mutation decision | none | unavailable; retries 1; corrections 0; reviews 1 |
| T6 | Final integrated regression, deployment, bounded recovery, and production verification. | Cheap verifier and Lead | gpt-6-luna high for mechanical gates; Lead for acceptance/operation | T1,T2,T3,T4,T5 and approval | tests/workflows/approved production operation | CI/deploy plus terminal resource evidence | CI/deploy passed; six priority-1000 recovery tasks succeeded; failures 0; pipeline resumed | Verified | Cheap verifier for gates; Lead for final acceptance | no delegated patch | unavailable; retries 1 external Terraform download retry; corrections 0; reviews 1 |
| T7 | Correct the bulk specific-resource input discovered during production recovery. | Cheap executor/verifier | gpt-6-luna high; local UI binding fix | T6 production exercise | Jobs UI and component test | component regression plus production browser verification | native `oninput`, current definition revisions, component regression, and production preview/submit verified | Verified | Cheap executor/verifier - closure item within approved operator recovery | no delegated patch | unavailable; retries 1; corrections 1; reviews 1 |
| T8 | Propagate stronger scheduling when a manual recovery reuses an active task. | Cheap executor/verifier | gpt-6-luna high; bounded persistence correction | T7 production exercise | collection platform store and focused regression | store regression plus production task detail | production recovery tasks were upgraded to Realtime/1000 without changing requested revision and were executed | Verified | Cheap executor/verifier - closure item within approved recovery | no delegated patch | unavailable; retries 1; corrections 1; reviews 1 |
| T9 | Recover legacy official Horse identities from their exact collection-resource metadata before profile projection exists. | Cheap executor/verifier | gpt-6-luna high; bounded identity evidence correction | T8 production exercise | collection platform read API, API identity integration, focused tests | production-shaped legacy-ID regression and mismatched-source counterexample | exact resource metadata resolves the matching official source while a different source remains rejected | Verified | Cheap executor/verifier - closure item within approved identity recovery | no delegated patch | unavailable; retries 1; corrections 1; reviews 2 |
| T10 | Release legacy dispatched Ready envelopes that permanently consume the single production dispatch slot without an execution lease. | Cheap executor/verifier | gpt-6-luna high; bounded schema recovery | T9 deployment observation | collection schema migration and dispatch regression | v19 fixture with one stranded legacy envelope becomes dispatchable while leased/current envelopes remain unchanged | schema-v20 regression passed; production dispatch resumption remains under T6 | Verified | Cheap executor/verifier - closure item required for production terminal verification | no delegated patch | unavailable; retries 1; corrections 1; reviews 2 |
| T11 | Adopt the first official JRA identity for a single source-less opaque legacy Horse aggregate. | Cheap executor/verifier | gpt-6-luna high; bounded identity fallback | T10 production replay | Horse resolver and focused tests | unique v4 opaque legacy candidate succeeds; multiple candidates and source-bound v5 namesakes remain rejected | focused 16 passed; API 411 passed/1 skipped; production replay remains under T6 | Verified | Cheap executor/verifier - closure item after exact-resource evidence proved unavailable for first-seen legacy Horses | no delegated patch | unavailable; retries 1; corrections 1; reviews 2 |
| T12 | Preserve the conflicting Horse name and normalized official source in isolated identity failure evidence. | Cheap executor/verifier | gpt-6-luna high; diagnostic contract preserving error code | T11 production replay | race identity rejection detail and regression | `HorseIdentityConflict` code remains stable while detail identifies the candidate | focused 17 passed; API 412 passed/1 skipped; build and format passed | Verified | Cheap executor/verifier - bounded observability closure item | no delegated patch | unavailable; retries 1; corrections 1; reviews 1 |
| T13 | Exclude superseded Horse aggregates from collection identity resolution. | Cheap executor/verifier | gpt-6-luna high; bounded read-model correction | T12 production evidence | Horse resolver load query and collected-race regression | a redirect source cannot create a false same-name identity conflict | focused 18 passed; API 413 passed/1 skipped; build and format passed | Verified | Cheap executor/verifier - closure item after exact Horse/source evidence | no delegated patch | unavailable; retries 0; corrections 0; reviews 1 |
| T14 | Release queue-proven orphaned Ready dispatches at the deployment boundary. | Cheap executor/verifier | gpt-6-luna high; safety-gated operational recovery | T13 production replay | store, pipeline pause endpoint, deploy guard, tests | release requires paused pipeline, no running lease, and empty visible/in-flight queue | deploy script passed; Collector 413 passed; API 413 passed/1 skipped; format passed | Verified | Cheap executor/verifier - closure item for repeated post-deploy capacity stall | no delegated patch | unavailable; retries 2; corrections 2; reviews 2 |
| T15 | Treat a structured JRA directory no-candidate result as unavailable rather than an actionable failure. | Cheap executor/verifier | gpt-6-luna high; bounded outcome correction | T14 production replay | subject handler and focused regression | `NoCandidate` becomes `NotApplicable`; ambiguity and structural failures remain fail-closed | focused 4 and Collector 414 passed; three production Horse recovery tasks succeeded without actionable failures | Verified | Cheap executor/verifier - closure item for repeated withdrawn/missing Horse profiles | no delegated patch | unavailable; retries 0; corrections 0; reviews 1 |
| T16 | Adopt a source-less Horse whose ID is derived from the exact legacy JRA CNAME route. | Cheap executor/verifier | gpt-6-luna high; bounded compatibility correction | T15 production replay | Horse resolver and positive/negative regressions | exact legacy route IDs reuse the aggregate; a different route remains a conflict | focused 11 and API 415/1 skipped passed; `20250810:Sapporo:8` succeeded in production | Verified | Cheap executor/verifier - closure item for legacy route-derived IDs | no delegated patch | unavailable; retries 1; corrections 1; reviews 1 |
| T17 | Release unleased Ready dispatch reservations while paused even when stale wakes remain in SQS. | Cheap executor/verifier | gpt-6-luna high; bounded recovery gate correction | T16 production replay | pipeline endpoint and existing lease-safety regression | active leases remain protected; stale wakes fail their reservation token and cannot execute | store safety, Collector 414, and API 415/1 skipped passed; production pause/resume released and replayed all six tasks | Verified | Cheap executor/verifier - closure item for priority bypass after deployment | no delegated patch | unavailable; retries 1; corrections 1; reviews 1 |
| T18 | Make priority outrank the persisted dispatch scan cursor and keep high-priority candidates inside the bounded page. | Cheap executor/verifier | gpt-6-luna high; bounded scheduler correction | T17 production replay and monitoring finding | allocator, pending query, and regression | priority 1000 before the cursor outranks priority 70 after it; lane fairness remains unchanged | focused 14 and Collector 415 passed; all six production priority-1000 tasks were dispatched and succeeded | Verified | Cheap executor/verifier - closure item for `DispatchOrderViolation` | no delegated patch | unavailable; retries 0; corrections 0; reviews 1 |

The read/write scopes overlap across the collection identity and persistence path, so implementation is
serialized. No subagent was dispatched for the current bounded investigation; the Lead retained the
public-contract and production mutation decisions, while planned mechanical implementation and
verification use the repository's requested cheap route after approval.

## Review gates

- **Design and task-split review:** Initial classification is complete, but T1 cannot be frozen until
  T4/T5 provide the exact finite production inventory. ACs cover happy path, namesake/ambiguity,
  partial failure/restart, request amplification, controlled recovery, and operator visibility.
- **Concern and agreement review:** Production inventory and live parser probes resolved C3/C4. The
  user's instruction to perform all corrections and production recovery approves the decisions and ACs.
- **Pre-implementation review:** Complete. T2 owned the handler, resolver, and focused tests. The frozen
  decisions required no public contract, schema, or definition-revision change. No production mutation
  occurs before CI/deployment. Scope expansion beyond exact pedigree tasks and three races returns to design.
- **Checkpoint review:** Complete for local implementation. Focused identity behavior passed before full
  regressions. The patch removes outbound pedigree identity calls, so it reduces rather than amplifies JRA/API
  traffic. No production mutation occurred and no group is claimed recovered yet.
- **Checkpoint review (T9):** Production task `20260503:Tokyo:6` reproduced `HorseIdentityConflict` for three
  Horses whose legacy aggregate IDs predate stable-number normalization. Their exact Horse collection resources
  already contain equivalent official JRA source identities, but `LoadHorsesAsync` reads only the not-yet-created
  profile projection. T9 may enrich only by exact `(Horse, JRA, HorseId)` resource metadata and must retain the
  different-source, source-less, and ambiguous fail-closed counterexamples.
- **Checkpoint review (T10):** After the T9 deployment and pipeline resume, production had zero running tasks,
  2,806 Ready tasks, and monitoring reported stalled priority-100 race work plus dispatch-order violations.
  Capacity is one envelope. The compatibility-era capacity query counts a legacy `DispatchedAt` Ready envelope
  forever when it has no execution lease; the wake protocol cannot reclaim that row because it is already marked
  dispatched. Schema v20 may reset only current-generation Ready rows with a legacy dispatched envelope, no
  `WakeId`, and no execution lease for that envelope. Running, leased, pending-wake, and completed rows are excluded.
- **Checkpoint review (T11):** The resumed dispatcher reached `20260531:Kyoto:6`, which failed before subject
  scheduling could create exact Horse collection metadata. The remaining candidate is a single same-name,
  source-less opaque legacy aggregate. T11 may adopt that aggregate only when it is the sole source-less namesake
  and birth evidence does not conflict; multiple candidates and any conflicting source-bound identity remain closed.
- **Checkpoint review (T13):** T12 identified `20260131:Kyoto:3` Horse `キャッチアシーフ` with official source
  `jra-horse:2023103810`. The public Horse search exposes one active aggregate, while the resolver loaded active and
  superseded redirect-source rows. Redirect sources are already hidden from business reads and must likewise be
  excluded before collection identity matching; redirect targets and all non-redirected namesakes remain unchanged.
- **Checkpoint review (T15-T18):** The remaining production failures separated into safe unavailable
  subject outcomes, exact legacy route-derived Horse identities, stale dispatch reservations, and a scan-cursor
  priority inversion. Each closure retained its existing fail-closed counterexamples. After deployment, a bounded
  pause/resume invalidated stale wakes; the corrected allocator dispatched all six priority-1000 recovery tasks.
- **Final review:** Complete. Every AC and task is `Verified`. CI and deployment passed for the final commit;
  the three race recoveries and three Horse recoveries reached `Succeeded`, the dashboard reported zero failures,
  and the collection pipeline remained unpaused. No acceptance-blocking external issue remains.

## Documentation updates

- No canonical public or operator contract changed. The implementation corrects compatibility handling,
  internal dispatch recovery, and existing UI input behavior within the contracts already described by
  `docs/22-collector-design.md` and `docs/20-admin-ui-design.md`; this change record carries the incident-specific
  decisions and production evidence.

## Verification record

- `codegraph status`: index up to date (1,482 files, 15,914 nodes).
- CodeGraph trace and literal search confirmed the producer, resolver, handler catch boundary,
  failure-impact classification, existing repair preview, and operator presentation paths.
- Authenticated production inventory at 2026-10-02 11:56 JST reconciled 12 obsolete pedigree resources,
  11 parent profiles blocked after persistence, and three race write failures. The safety pause was active.
- Live parser probes under `probe/` confirmed official Horse identities for all rows of the three affected
  races: 18 (`20160417:Nakayama:11`), 16 (`20260503:Tokyo:6`), and 14 (`20260531:Kyoto:6`).
- `dotnet test tests/HorseRacingPrediction.Api.Tests/... --filter CollectionIdentityResolverTests`: 13 passed.
- `dotnet test tests/HorseRacingPrediction.Collector.Tests/... --filter JraSubjectCollectionHandlerTests`: 45 passed.
- Full API suite: 407 passed, 1 skipped. Full Collector suite: 411 passed.
- Schema-v20 dispatch recovery regression passed; the full API suite then passed 409 tests with 1 skipped.
- Initial deployment run 36971308157 exposed three Collector migration fixtures still pinned to schema v19;
  after updating only those fixture expectations/history downgrades, the full Collector suite passed 412 tests.
- `dotnet build HorseRacingPrediction.sln --no-restore` passed with zero warnings and errors;
  `dotnet format HorseRacingPrediction.sln --verify-no-changes --no-restore` and `git diff --check` passed.
- T11 identity and collected-race guards: 16 passed. The full API suite passed 411 tests with 1 skipped;
  solution build and formatter verification remained clean.
- T12 diagnostic identity guards: 17 passed. The full API suite passed 412 tests with 1 skipped; solution build and
  formatter verification remained clean. The rejection retains `HorseIdentityConflict` as its machine-readable
  code while its isolated detail identifies the conflicting Horse name and normalized official source.
- T13 redirect-source exclusion: focused identity guards passed 18 tests. The full API suite passed 413 tests with
  1 skipped; solution build and formatter verification remained clean. The regression proves that collection writes
  retain the active redirect target and never reselect the superseded source aggregate.
- T14 deployment-boundary dispatch recovery: the focused store and deployment-contract tests passed; the deployment
  script harness passed every positive and fail-closed case. Full Collector tests passed 413 tests and full API tests
  passed 413 tests with 1 skipped; formatting verification remained clean.
- CI run 36958984104 and deployment run 36958984094 passed; the deployment drain step and API health check succeeded.
- Production recovery closed exactly 12 obsolete pedigree failures, queued 11 parent profiles and three races,
  resumed the pipeline, and reconciled zero actionable failure groups. Exercising the bulk specific-resource
  flow then reproduced a stale UI binding: the visible IDs were not available to preview validation. An initial
  `Immediate` correction passed component tests and CI but failed its production browser counterexample. T7
  therefore uses a native `oninput` binding and exercises that actual DOM event in the component test. The
  resulting request exposed a second stale default: manual and bulk requests always sent revision 1 although
  the deployed `race-detail` revision is 6 and subject definitions are also newer. T7 now derives the revision
  from `CollectionDefinitionRevisions` for both request paths and asserts the serialized request revision.
- The deployed T7 build passed CI run `36964460163` and deployment run `36964460175`. Production bulk preview
  accepted all four supplied race IDs and submission completed. A subsequent individual priority-100 request
  exposed that the non-ordinary active-task reuse branch retained the original Normal/50 scheduling. T8 updates
  only the reused task lane and priority; it deliberately leaves the task revision unchanged so the existing
  higher-revision materialization path remains intact.
- T9 focused regression: 8 passed. Full API regression: 408 passed, 1 skipped. Solution build completed with
  zero warnings and zero errors; `dotnet format --verify-no-changes`, `git diff --check`, and the existing
  official-ID/name-only counterexamples passed. The first focused run exposed an over-broad metadata enrichment;
  the implementation was narrowed to legacy aggregate IDs that differ from the deterministic ID derived from
  the exact stored official source, then all existing and new counterexamples passed.
- Solution build completed with zero warnings and errors; `dotnet format --verify-no-changes --no-restore`,
  `git diff --check`, and the change-record audit validator passed.
- T15 structured no-candidate handling: focused 4 passed and full Collector passed 414 tests. T16 legacy route
  identity adoption: focused 11 passed and full API passed 415 tests with 1 skipped. T17 lease-safe paused release:
  focused store safety passed, Collector passed 414, and API passed 415 with 1 skipped. T18 priority/cursor ordering:
  focused 14 passed and full Collector passed 415; solution build, formatter, and diff hygiene remained clean.
- Final CI run `36993397675` and deployment run `36993397688` passed for commit `e52b497a`. The first deployment
  attempt encountered a transient Terraform provider download connection reset; retrying only failed jobs completed
  the same commit without a code or configuration change.
- At 2026-10-02 19:29 JST, production tasks for `20250810:Sapporo:8`, `20260503:Tokyo:6`, and
  `20260131:Kyoto:3` had each completed successfully with one attempt and no error code. Horse tasks
  `1bb7c5dd-1122-4051-aa68-d2499a66ec97`, `5a648bfe-fd4c-40c7-9428-4eb55c546ee8`, and
  `ec1d1bfe-7dc1-4eff-8c4a-10bce3770f5e` had also completed successfully with one attempt.
- The final production dashboard reported zero failures, 736 current resources, and 16 unavailable resources.
  The pipeline was unpaused with no pause reason. Historical monitoring findings remain historical evidence; the
  formerly stalled priority-1000 task IDs are terminal and no new actionable failure group is present.
- No secret was written to commands, logs, documentation, or commits.

## Deviations and follow-up

No acceptance-blocking deviation remains. The historical monitoring window can continue to display earlier
`DispatchOrderViolation` evidence until it ages out; terminal task state and post-deployment dispatch behavior are
the authoritative recovery evidence. Normal backlog processing continues with the pipeline running.
