# 現在の収集エラーの原因解消と対象復旧

- Status: Proposed
- Change record schema: 2
- Owner: Lead
- Created: 2026-10-24
- Updated: 2026-10-24

## CURRENT

Authenticated diagnosis complete:46 distinct current resource/task failures,41 source-less legacy horse-ID conflicts,4 ambiguous sire-profile identities,1 historical retired-trainer navigation failure. Read all46 histories/399attempts,16current failure batches,41exact-name horse lookups,41per-ID diagnostics,4origin horse profiles,trainer profile and pipeline state. Current collection is automatically paused since24Oct19:55:07 by the new Niigata8 HorseIdentityConflict, not the older deployment pause. No production mutation/code edit. Receipts and candidate inputs saved without credential; see api-observation.md, failure-receipt.json and diagnostic-candidates.json. T1/T2 Verified. T3 proposes expanding selected migration inputs beyond original3 to current41; real-world identity is still a hypothesis, requiring explicit approval below. No reset/guard bypass/blanket retry. Other5 stay separately owned and unfinished. Original3 operation remains unfinished in parent; do not mistake this diagnosis for recovery.

## Hypotheses and concerns

- H1 resolved: all41 race names have one exact-name current horse, Supported unchanged registrations, no source identity/birth date, differing old name-derived IDs; persisted latest failures and actual resolver guard corroborate unproven legacy-ID adoption. This proves the rejection mechanism, not real-world identity of proposed pairs.
- H2 resolved:46 unique resources and46 tasks at this observation. Exact reason for difference from earlier44 is unavailable; no inferred delta cause.
- C1 Open decision: whether to accept the41 hypothesis mappings beyond original3. Evidence: missing original official identities/birth dates means unique current name plus incoming official source is not conclusive identity proof. Impact: wrong merge could affect historical references. Recommendation: use existing generic migration with explicit selected candidate inputs, backup, stopped full reference validation, collision fail-closed, per-operation postchecks; alternative is manual external identity confirmation before migration. Residual risk: internally consistent migration does not prove real-world identity. AC2/AC3,T3/T5; counterexample same-name different real horses; agent position no guard removal/reset, no apply before approval; user disposition Open decision. Broad permission to risk the environment is not recorded as approval of these particular41 mappings.

## Proposed recovery decision (T3, awaiting approval)

Expand operator-selected migration scope to the41 diagnostic candidates, not compiled presets and not a DB reset. Re-read current targets before use; candidate JSON is a receipt, not an executable or approved manifest. Use official source identities reported by race attempts plus canonical old horse data to prepare inputs. Keep existing max16-operation bound, stop/drain/backup, full typed-reference and collision checks, rollback/idempotency and result checks unchanged. Do not force unknown/ambiguous data through; report excluded/conflicting pairs. After verified migration, retry only eligible affected tasks; a race may expose another horse conflict after its first one is corrected, so do not promise41 mappings exhaust all latent failures. Collection resume remains a separate approved operation and requires actual successful replacement/progress evidence.

The4 ambiguous sire profiles require fresh candidate evidence/candidate-bound repair; the trainer requires current navigation reproduction and a bounded correction if still reproducible. Do not dismiss these5 or claim the whole incident resolved after the41 migration. Any new production-code/public-contract change still needs a concrete scoped design and code gate; no such write scope has been released.

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Current groups/targets/tasks/counts and persisted causes reconcile without guessing missing evidence | T1,T2 | GET group/detail/batch evidence plus exact source contract | Verified |
| AC2 | Each in-scope deterministic cause has an approved simple correction and regression evidence, not merely a retry | T2,T3,T4 | Real producer/persistence/consumer path and counterexample tests | Not started |
| AC3 | Corrected eligible work succeeds and collection progresses; history retained, no blanket reset/hidden failures | T5 | Actual migration/postchecks/replacement tasks and lane timestamps | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Capture live scoped API inventory and deduplicate targets | Lead | Lead | none | Authorized read-only production API and records | GET status/count/group/resource/history/batch evidence; no secrets/raw bodies | api-observation.md and saved sanitized receipts;46unique targets,399attempts,16batches,41diagnostics | Verified | Lead — credential privacy and production evidence acceptance | none | unavailable; retries 0; corrections 0; reviews 0 |
| T2 | Trace four observed failure codes and API contracts | pending_planner | Luna/high | none | Read-only collection contracts, handlers and identity validation | Exact routes/wrappers/fields, caller traces, source test locations | Source report independently corroborated by Lead resolver/DTO reads and actual API contracts | Verified | Worker — bounded repository investigation | T2-A1 | unavailable; retries 0; corrections 0; reviews 1 |
| T3 | Freeze corrective scope and data safety decisions | Lead | Lead | T1,T2 | This record and affected canonical design docs | Fact/hypothesis reconciliation, concerns, scoped acceptance and approval | Proposed41-input migration expansion; C1 user identity-risk decision required | Proposed | Lead — architecture/data/approval decisions | none | unavailable; retries 0; corrections 0; reviews 0 |
| T4 | Implement and verify approved correction | Planned Luna executor | Luna/high | T3 and explicit approval | No write scope released; freeze after actual cause is known | Relevant red/green, formatter/build/regression and exact snapshot | No implementation authorized yet | Dependent | Worker — bounded implementation after design | none | unavailable; retries 0; corrections 0; reviews 0 |
| T5 | Deploy if needed and recover eligible targets | Lead | Lead | T3,T4 or applicable existing approved operation | Authorized deployment/API operations and records | Backup/safe-state/apply/postchecks/selected retry/separate resume/progress | No mutation yet | Dependent | Lead — production/data/final acceptance | none | unavailable; retries 0; corrections 0; reviews 0 |

## Documentation updates and review

Current architecture/behavior is not changed during read-only investigation. T3 will update affected canonical docs before any new design approval. Orchestration/DDD/incident skills loaded in this turn. Read-only T2 needs no code edit, test change or full suite; executable source queries and exact current contracts are its evidence. No new public contract or production code may be changed while Proposed. This record owns resolution, not just investigation; all unknown causes remain unfinished. Main preserves userdirty AGENTS/skills/policy tests and prior parent handoff.

Checkpoint/final diagnostic review24Oct: canonical audit validator exit0; sanitized JSON counts41/46, unique46tasks/resources and candidate identifier shapes PASS; git diff --check exit0. No product source changed, so build/regression is not claimed or replayed for this read-only receipt. T1/T2 source/API evidence independently reviewed; AC1 Verified, AC2/AC3 still incomplete. Secure credential disposed and process exited0. Next after explicit C1 decision: fresh candidate revalidation, existing generic migration admission/operation path with unchanged safety gates; separately scope remaining5 cause repairs. No background agent or continuing production operation is running while awaiting that decision. Unrelated dirty policy/AGENTS/routing/efficiency files remain outside this diagnostic checkpoint.

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | Current causes/corrective scope not frozen |
| Verification | In progress | API evidence and source traces |
| Deployment/operation | Not started | Read-only only; original3 operation not executed |
