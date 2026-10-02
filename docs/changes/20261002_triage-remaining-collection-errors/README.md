# Remaining collection error triage

- Status: Proposed
- Change record schema: 2
- Owner: Collection operations
- Created: 2026-10-02
- Updated: 2026-10-02

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | No production code change is authorized by this investigation. |
| Verification | In progress | Current group-level evidence and code paths are classified; per-resource authenticated diagnostics remain. |
| Deployment/operation | Not started | No retry, dismissal, merge, suppression, or pipeline mutation was performed. |

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

1. Introduce a separately persisted, idempotent pedigree-reference discovery outcome containing the
   parent Horse ID, relation (`Sire`/`Dam`), normalized name, source page, and failure code. It must not
   invent a Horse ID or source identity.
2. Complete the parent horse-profile task once profile persistence succeeds. A single unresolved optional
   reference must not prevent the remaining references or race-history discovery. Unresolved references
   remain operator-visible and independently retryable; they are not silently dropped.
3. Resolve a pedigree Horse to an authoritative Horse only after official JRA source identity is obtained.
   The preferred implementation is a two-phase discovery task (name search/profile selection, then
   source-bound upsert), not a relaxation of `CollectionIdentityResolver`.
4. Classify the 12 no-candidate resources through the existing repair preview. Apply only evidence-backed
   outcomes: safe merge, validated official URL, proven obsolete/generated reference dismissal, or remain
   blocked. Same-revision blind retry remains prohibited.
5. Diagnose the two race write failures individually. Use the existing fenced assignment/identity repair
   only when read-back proves two persisted identities; otherwise perform one controlled retry after
   confirming the failure attempt predates the deployed normalizer.

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | Treating pedigree names as authoritative Horse identity can merge namesakes. | Silent cross-horse corruption. | Keep source-bound resolution and fail closed; separate discovery from upsert. | AC2/T2; namesake counterexample | Evidence-based objection to name-only fallback | Pending | Resolved in design |
| C2 | Marking the parent successful could hide missing pedigree data. | Partial data becomes invisible. | Persist relation-level outcomes and show unresolved counts before decoupling task success. | AC1/AC3/T1-T3 | Agree only with durable visibility | Pending | Resolved in design |
| C3 | Twelve no-candidate items may have mixed causes. | Bulk dismissal or retry can lose evidence or create loops. | Require exact per-item classification through the existing preview. | AC4/T4 | Object to group-wide mutation | Pending | Open decision |
| C4 | The two race errors may be stale or represent real duplicate identities. | Blind retry may repeat deterministic rejection; blind repair may rewrite correct assignments. | Read current attempt and assignment evidence first; use one controlled action per proven class. | AC5/T5 | Object to retry before evidence | Pending | Open decision |
| C5 | Two-phase JRA discovery increases requests during existing access limiting. | More 429/transient responses and queue pressure. | Cache/deduplicate discovery by normalized name and bound concurrency; verify request counts. | AC6/T2/T3 | Agree with bounded design | Pending | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | A persisted parent horse profile can finish race-history discovery even when one pedigree reference cannot be resolved; the unresolved relation remains durable and visible. | T1-T3 | Production-shaped handler integration test with one failing and one succeeding reference | Not started |
| AC2 | No Horse is created, selected, or merged from a pedigree name alone when official/source-bound same-name records exist or candidates are ambiguous. | T2 | resolver/handler counterexamples for namesake, ambiguity, and conflicting birth evidence | Not started |
| AC3 | Reference discovery is idempotent, independently retryable, and preserves parent/relation/source provenance without duplicate work. | T1-T3 | persistence, replay, partial-failure, and restart tests | Not started |
| AC4 | All 12 `SubjectNotIdentified` resources are inventoried as safe recovery, obsolete dismissal, or blocked with explicit evidence; no group-wide blind action occurs. | T4 | authenticated read-only diagnostics and repair-preview reconciliation | Not started |
| AC5 | Both race write failures have individual root-cause evidence and only the matching fenced recovery is executed; persisted result/lifecycle evidence is read back. | T5 | resource/attempt/batch/assignment evidence plus controlled production verification | Not started |
| AC6 | The correction does not materially amplify JRA requests; concurrency, deduplication, 429 handling, and pipeline safety-stop behavior remain verified. | T2-T3 | request-count, retry classification, dispatcher, and production observation | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Define relation-level discovery persistence/API and exact operator projection. | Lead planner | High capability; persistence/public-contract decision | T4,T5 | change record and contract design only | schema/entry-point inventory | settled contract and migration/rollback plan | Dependent | Lead - public contract and persistence decision | none | unavailable; retries 0; corrections 0; reviews 1 |
| T2 | Implement two-phase source-bound pedigree discovery without relaxing identity guards. | Cheap executor | gpt-6-luna high; bounded implementation after contract freeze | T1 | Collector, Contracts, API identity integration, focused tests | focused contract/handler/resolver tests | passing production-shaped counterexamples | Dependent | Cheap executor - frozen, independently testable implementation | pending dispatch | unavailable; retries 0; corrections 0; reviews 0 |
| T3 | Integrate partial completion, idempotency, retry and operator visibility. | Cheap executor | gpt-6-luna high; bounded implementation after T1/T2 | T1,T2 | Collection Platform and admin projection, tests | persistence/restart/UI contract suites | observable unresolved relation and successful parent continuation | Dependent | Cheap executor - frozen integration after T1/T2 | pending dispatch | unavailable; retries 0; corrections 0; reviews 0 |
| T4 | Inventory and classify the 12 no-candidate resources read-only. | Cheap investigator | gpt-6-luna high; read-only evidence gathering | authenticated session | production GET/repair preview only | exact 12-item reconciliation | finite inventory with evidence category per resource | Externally blocked | Cheap investigator - bounded read-only inventory | pending dispatch | unavailable; retries 0; corrections 0; reviews 0 |
| T5 | Diagnose the two race write failures and choose controlled recovery per item. | Cheap investigator, Lead accepts recovery | gpt-6-luna high; read-only first, Lead owns mutation decision | authenticated session | production GET only until evidence is reviewed | two complete resource/attempt/batch/assignment traces | two item-level cause and recovery decisions | Externally blocked | Cheap investigator for evidence; Lead for mutation decision | pending dispatch | unavailable; retries 0; corrections 0; reviews 0 |
| T6 | Final integrated regression, deployment, bounded recovery, and production verification. | Cheap verifier and Lead | gpt-6-luna high for mechanical gates; Lead for acceptance/operation | T1,T2,T3,T4,T5 and approval | tests/workflows/approved production operation | CI/deploy plus terminal resource evidence | passing gates and production terminal evidence | Dependent | Cheap verifier for gates; Lead for final acceptance | pending dispatch | unavailable; retries 0; corrections 0; reviews 0 |

The read/write scopes overlap across the collection identity and persistence path, so implementation is
serialized. No subagent was dispatched for the current bounded investigation; the Lead retained the
public-contract and production mutation decisions, while planned mechanical implementation and
verification use the repository's requested cheap route after approval.

## Review gates

- **Design and task-split review:** Initial classification is complete, but T1 cannot be frozen until
  T4/T5 provide the exact finite production inventory. ACs cover happy path, namesake/ambiguity,
  partial failure/restart, request amplification, controlled recovery, and operator visibility.
- **Concern and agreement review:** C3 and C4 remain `Open decision`; approval is not requested yet.
- **Pre-implementation review:** Not applicable until the record is approved.
- **Checkpoint review:** Group-level production evidence and current source paths were compared. No
  mutation occurred and no group is claimed recovered.
- **Final review:** Pending.

## Documentation updates

- No canonical documentation is changed during the incomplete investigation. Inspected
  `docs/22-collector-design.md`, `docs/20-admin-ui-design.md`, and the existing subject-identification
  recovery change records. If approved, the final design must update the collector and operator
  documents with the new relation-level recovery contract.

## Verification record

- `codegraph status`: index up to date (1,482 files, 15,914 nodes).
- CodeGraph trace and literal search confirmed the producer, resolver, handler catch boundary,
  failure-impact classification, existing repair preview, and operator presentation paths.
- Production dashboard evidence was last captured at 2026-10-02 11:39 JST before the authenticated
  browser session expired. No secret was written to commands, logs, documentation, or commits.

## Deviations and follow-up

The current browser session is no longer authenticated. T4 and T5 require a new authenticated read-only
session to collect the finite item inventory. The next operation is to read the three failure groups and
their bounded resource/attempt/batch histories; it is not to retry or dismiss them.
