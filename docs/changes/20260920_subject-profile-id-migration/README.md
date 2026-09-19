# Subject profile job ID migration

- Status: Proposed
- Change record schema: 2
- Owner: HorseRacingPrediction team
- Created: 2026-09-20
- Updated: 2026-09-20

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | Production changes wait for explicit approval. |
| Verification | Not started | Classification, idempotency, failure/restart, and regression tests are planned. |
| Deployment/operation | Not started | Production preview/apply is outside this implementation and requires a later explicit operation. |

## Context

Historical race-derived Horse/Jockey/Trainer profile tasks can retain a name and provenance even when their resource ID no longer matches the API-authoritative subject. Retrying JRA collection or URL fallback cannot repair a wrong persistence target. Conversely, these tasks are not uniformly meaningless: the current RaceEntry and subject projections can often identify the correct target.

The existing obsolete-task endpoint only selects active `SubjectProjectionNotReady` tasks and cancels tasks whose resource is absent or no longer referenced. It does not recover a canonical target, omits terminal `SubjectResourceMissing` tasks, and can discard useful migration evidence. Existing Horse identity repair provides safe merge patterns, but there is no equivalent migration classification for all three subject types.

## Goals

- Classify historical race-derived profile tasks from durable evidence before cancellation or retry.
- Automatically migrate only uniquely proven old-ID to current-ID mappings.
- Preserve old requests, tasks, attempts, and failure evidence; never rewrite a task resource ID.
- Create or reuse a Recovery task for the canonical target before retiring the obsolete source work.
- Keep ambiguous, inconsistent, or projection-incomplete cases visible for repair or manual review.

## Non-goals

- Global name-only merging.
- Automatic mutation of RaceEntry identity.
- Deleting collection history.
- Running the migration in production as part of this code change.
- Changing collection priority; that remains a separate scheduling concern.

## Decisions

### Candidate scope

Inspect race-derived `horse-profile`, `jockey-profile`, and `trainer-profile` tasks whose evidence contains a nonblank `requestedByRaceId` and whose latest relevant failure is `SubjectProjectionNotReady` or `SubjectResourceMissing`. Include active and terminal failed/dead-letter tasks so a newer permanent classification does not hide historical work.

### Identity evidence

Task `name` is a join/check, not sole proof. Resolve only inside the stated race and subject type. A target is auto-migratable when exactly one current RaceEntry reference satisfies all of:

1. its current subject ReadModel exists;
2. canonical subject names match;
3. its ID equals the current deterministic ID derived from the canonical name;
4. for Horse with a valid JRA `CNAME` identity, the target ID also matches that identity-derived ID.

Jockey/Trainer profile URLs remain validated collection locators, not identity proof. An invalid/stale locator is omitted from the Recovery request so normal official discovery provides the URL fallback.

### Classification

| Classification | Meaning | Execute behavior |
| --- | --- | --- |
| `AutoMigrate` | One different, existing, race-referenced target satisfies every identity gate. | Create/reuse target Recovery, then retire/suppress obsolete source work. |
| `RetryExisting` | Source ID is already the proven current target. | Do not migrate; diagnose/retry the existing resource. |
| `AlreadyCurrent` | Proven target profile is already current. | Supersede obsolete work without redundant collection. |
| `RepairProjection` | Race references a target ID whose subject projection is missing. | Do not enqueue profile work; report prerequisite subject repair. |
| `Ambiguous` | More than one distinct target satisfies name evidence. | No mutation; manual review. |
| `IdentityConflict` | Horse identity, deterministic ID, name, or race reference conflict. | No mutation; correct upstream identity/race evidence. |
| `Unverifiable` | Name/race/evidence is missing or no in-race target can be proven. | No automatic cancellation or migration. |

### Apply semantics

- Preview is read-only and returns evidence, target, classification, and blocking reason.
- Apply accepts explicit candidate identifiers and re-runs every gate against current data.
- Create/reuse the target Recovery task before retiring source work. Use a deterministic migration key derived from source task, target, and revision.
- Preserve all source history. Suppress an entire source resource only when it is proven phantom and no RaceEntry references it anywhere; otherwise retire only the selected task.
- Repeated apply reuses the target task and reports an already-completed migration without duplication.
- A running source task receives cancellation; completion racing with apply must not re-enable obsolete work.

## Technical impact

- Extend collection maintenance models with bounded migration evidence and classification results.
- Extend `CollectionPlatformStore` selection/retirement primitives without changing existing task identity.
- Replace the obsolete cleanup decision with API-owned classification using RacePredictionContext and Horse/Jockey/Trainer projections.
- Reuse current normalization, Recovery request, suppression, and Horse identity safeguards.
- Retain the existing maintenance endpoint path where compatible; make response changes explicit in tests. No UI is included.

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | Names are not globally unique; Jockey/Trainer URLs do not prove the persistence ID. | A name-only merge can corrupt identity. | Require one in-race referenced target plus name/current-ID agreement; Horse identity is an additional hard gate. | AC2/T2; ambiguity and identity-conflict tests | Agree | Pending | Resolved in design |
| C2 | Event-store projections and collection storage do not share a transaction. | Evidence may change between preview and apply. | Revalidate at apply, create target first, then retire source; deterministic key makes replay safe. | AC4/T3; stale-preview and restart tests | Agree | Pending | Resolved in design |
| C3 | Resource suppression affects every definition/task for an ID. | A valid source could be disabled too broadly. | Suppress only a proven phantom with zero RaceEntry references; otherwise retire only the selected task. | AC5/T3; remaining-reference test | Agree | Pending | Resolved in design |
| C4 | Terminal `SubjectResourceMissing` tasks currently escape cleanup. | Recoverable evidence remains stranded. | Include relevant active and terminal tasks while preserving history. | AC1/T1; terminal-selection test | Agree | Pending | Resolved in design |
| C5 | Automatically repairing missing subject projections would broaden domain mutation. | A maintenance operation could invent domain entities. | Classify as `RepairProjection`; do not create subjects or profile jobs until the authoritative projection is repaired. | AC3/T2; missing-projection test | Agree | Pending | Resolved in design |
| C6 | Production apply is destructive operational work. | Incorrect execution could retire live tasks. | Deliver dry-run/apply code and tests only; production execution requires separate explicit authorization and reviewed preview. | AC6/T4 | Agree | Pending | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Preview includes race-derived active `SubjectProjectionNotReady` and terminal `SubjectResourceMissing` tasks, preserves name/source/provenance evidence, and excludes unrelated work. | T1,T2 | Store and endpoint tests. | Not started |
| AC2 | Horse/Jockey/Trainer migrate automatically only for one existing target referenced by the stated race with canonical-name and deterministic-ID agreement; Horse identity conflicts block. | T2 | Positive and counterexample classification tests. | Not started |
| AC3 | Same-ID, already-current, projection-missing, ambiguous, conflicting, and unverifiable cases receive distinct non-destructive classifications. | T2 | Classification matrix tests. | Not started |
| AC4 | Apply revalidates evidence, creates or reuses the canonical Recovery task before retiring source work, and is idempotent after partial/repeated execution. | T3 | API/store integration and restart tests. | Not started |
| AC5 | Source history is retained; resource-wide suppression occurs only with zero remaining RaceEntry references; running-task cancellation cannot restore obsolete work. | T3 | Persistence, concurrency, and history assertions. | Not started |
| AC6 | The operation remains dry-run-first, requires explicit candidate selection, exposes blocking reasons, and performs no production execution in this change. | T2,T3,T4 | Endpoint contract tests and final review. | Not started |
| AC7 | Existing authoritative race-card profile-job creation, URL fallback, Horse repair, and profile failure classification regressions continue to pass. | T3,T4 | Focused suites plus solution build/format gates. | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Extend migration candidate/result models and store selection evidence. | Worker | Worker | Approval | Collection models/store and store tests | Focused store tests | Selection and evidence fixtures pass. | Proposed | Worker — bounded persistence slice after contract freeze | none | unavailable; retries 0; corrections 0; reviews 0 |
| T2 | Implement API-owned classification and dry-run contract for all subject types. | Main | Lead | T1 | Collection maintenance endpoint/contracts and API tests | Classification matrix through endpoint | Every classification and counterexample is observable. | Proposed | Lead — identity and public-contract decisions | none | unavailable; retries 0; corrections 0; reviews 0 |
| T3 | Implement revalidated, idempotent apply and source retirement. | Main | Lead | T2 | Store/API repair path and integration tests | Apply/replay/race/history tests | Target-first recovery and safe retirement pass. | Proposed | Lead — persistence, concurrency, destructive boundary | none | unavailable; retries 0; corrections 0; reviews 0 |
| T4 | Integrate, sync CodeGraph, run regressions, and independently review. | Review worker; Main integrates | Review/Lead | T1-T3 | read-only review; record updates by Main | Focused suites, format, build, validator, graph trace | Independent verdict and final reconciliation. | Proposed | Reviewer — independent data-integrity challenge | none | unavailable; retries 0; corrections 0; reviews 0 |

## Review gates

- **Design and task-split review:** Main owns identity rules, endpoint contract, destructive semantics, integration, and final acceptance. The bounded store-selection slice can be delegated after approval; independent final review is read-only. Write scopes are serialized because store models are shared.
- **Concern and agreement review:** C1-C6 cover identity ambiguity, cross-store consistency, suppression breadth, terminal-task visibility, domain-repair boundary, and production authority. No unresolved technical decision remains; user disposition is pending.
- **Pre-implementation review:** Pending approval. Production code must not change before this gate.
- **Checkpoint review:** Pending.
- **Final review:** Pending.

## Documentation updates

- This record is the canonical design for ID migration of obsolete subject-profile jobs.
- After implementation, `docs/changes/20260919_collector-cost-reduction/README.md` will receive a follow-up link clarifying that its original cleanup is superseded by evidence-based migration classification; historical decisions will not be rewritten.
- No architecture document currently defines this maintenance workflow; no additional canonical document is required.

## Rollback

Revert the new classification/apply implementation while retaining old task history and migration audit data. Because production execution is excluded, code rollback precedes any separately authorized production apply. If a post-deployment preview produces unexpected ambiguity or conflicts, do not execute and reconsider the evidence rules.
