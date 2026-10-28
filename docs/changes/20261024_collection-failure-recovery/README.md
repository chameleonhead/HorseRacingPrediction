# 現在の収集エラーの原因解消と対象復旧

- Status: Approved
- Change record schema: 2
- Owner: Lead
- Created: 2026-10-24
- Updated: 2026-10-28

## CURRENT

28Oct17:49JST: executorの前回利用上限停止から、表示再開時刻経過を確認して既存Luna経路を再起動した。取消childはpreclosure全体2106成功/8skip・native成功まで到達したが、Lead最終点検で例外時lease解放・保護host維持・証明読取り専用化を追加修正中。文字列保存のRunning件数反例→最終CI/native→保護配備→条件付き明示取消の順。未配備/未移行/未再開を維持。28Oct08:51UTC health200はサイト応答の証拠だけで、移行停止や取消適格性は証明しない。

27Oct: 利用者が停止確認と安全確認後の準備中止に「適切な対応をお願いします」と承認。[取消record](../20261019_pre-apply-cancel/README.md) Approved、T2 journal/policyからLuna/highで直列実装する。T7は承認待ちではなく実装・統合検証待ち。現在の停止/取消適格性は実行時に証明する。以下26Oct handoffは当時の履歴。無条件解除/再実行/リセット/収集再開を行わない。

26Oct handoff: [migration error containment](../20261025_migration-error-containment/README.md) and [operator-only bootstrap](../20261025_operator-ui-bootstrap/README.md) Implemented; actual d020 image/run40301038134 success, backup/binding/pause/authenticated status+HTML/business503 verified09:53:22UTC, remote2088pass/8knownoptional. Parent remainsApproved: f8 StoppingApi, zero confirmedmigrated; no replay/reset/resume. T4-A2 current/old source contractsVerified. [Safe preparation cancellation](../20261019_pre-apply-cancel/README.md) refreshed, stillProposed. T7 externally blocked on explicit approval of new cancel interface/state/cleanup authority, not AWS access. After approval: journal policy→controller/gates/target bind→UI→native/CI/deploy/eligible explicit cancel; never blind replay. Owned implementation/evidence/design committed and published to codex/migration-error-containment, actual deployed source remainsd020 despite later docs commits. Only unrelated skills/AGENTS/olderdocs dirt remains. Parent41 migration, other5 causes, original handoff cause and collection progress remainunfinished.

User explicitly approved41-candidate migration expansion and requested direct API execution with the previously supplied key. T3 Verified/C1 Accepted risk; stop/drain/backup/full validation/postchecks remain mandatory. Lead owns credentials, live migration and acceptance; Luna verifies existing API contracts read-only. Next refresh groups/pipeline and candidate roots, stage generic max16-input batches, explicitly execute and poll actual operations through backup/apply/postchecks/restart, then eligible retry/separate resume/progress. No reset/guard bypass/forced unknown data. Other5 causes remain separate unfinished tasks; original3 in parent not silently declared done. Preserve unrelated dirty policy/AGENTS/routing/efficiency files. No production-code change authorized by this operational approval.

Execution24Oct21:17JST: fresh groups unchanged2+39+4+1, pipeline remains paused from19:55 incident. TargetReviewV2 batches16/16/9 all CanSavePending=true/no blockers. Stable request keys recorded in migration-requests.json before admission. First operation f8f857b4-23c6-40d7-ac04-60676f42aa6d staged PendingV2/AwaitingActions; execute next. Secure session71164 holds key only in memory. Target review/admission is not apply proof. No stage for later batches until first terminal result.

LATEST21:23JST: execute accepted21:19:36; first operation remains AwaitingActions/StoppingApi, results empty. Confirmed /health200, operation status200, ordinary pipeline/runtime503; UTF8-decoded problem title exactly `Application startup is blocked by migration state.` proves blocked host. ApiExited/BackedUp/Applying not observed; no migration completion is claimed. No repeat execute, later stage, retry, resume, verify-ineligible operation, reset or gate removal. Existing blocked-host API exposes status and post-success verification only, not pre-apply continuation. T5/T7 Externally blocked on runtime evidence/recovery outside the explicitly selected API route; seek permission for server-side diagnosis, retain same operation/request key. Specific child-exit/gate/supervisor cause remains unknown; do not assert exit73 was lost. Safe next action is controlled server logs/process/journal/marker metadata diagnosis, not a blind redeploy or migration replay. Later batches remain unsubmitted. The unfinished original3 operation and other5 failures share this access blocker.

Handoff: SecureString disposed, session71164 exited0; no background mutation continues. Existing operation/request keys and sanitized evidence are durable. Audit validator0, corrected skill-local change-record validator0 (initial root script path was absent; initial concern-heading check corrected without changing decision), diff checks0. Product source unchanged; owned diagnostic/operation documents are the checkpoint only.

Approval24Oct: user「お願いします。移行作業はAPIキーを使用し、サイトに対して直接操作する形で対応をお願いします。(API呼び出しの想定)」confirms preceding41-input migration proposal including its identity-risk boundary. Direct authenticated API is the selected operational route, not browser/AWS/SSH or GitHub dispatch.

## Hypotheses and concerns

- H1 resolved: all41 race names have one exact-name current horse, Supported unchanged registrations, no source identity/birth date, differing old name-derived IDs; persisted latest failures and actual resolver guard corroborate unproven legacy-ID adoption. This proves the rejection mechanism, not real-world identity of proposed pairs.
- H2 resolved:46 unique resources and46 tasks at this observation. Exact reason for difference from earlier44 is unavailable; no inferred delta cause.
- C1 Accepted risk: user explicitly approves41 hypothesis mappings beyond original3. Evidence: missing original official identities/birth dates means unique current name plus incoming official source is not conclusive identity proof. Impact: wrong merge could affect historical references. Use existing generic migration with explicit selected inputs, backup, stopped full reference validation, collision fail-closed, per-operation postchecks; alternative manual external confirmation deferred by approval. Residual risk: internal consistency is not real-world identity proof. AC2/AC3,T3/T5; counterexample same-name different real horses; no guard removal/reset. Monitor canonical identity/postchecks and successful replacement tasks; conflicting current identity or failed verification stops further batches and preserves backup for reviewed rollback, not automatic destructive reset.

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Disposition and monitoring | AC/task/counterexample | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | Missing old official identities/birth dates; details and alternative above | Potential wrong historical merge | Keep existing safety/backup gates; stop on conflict or failed postcheck and review rollback | AC2/AC3,T3/T5; same-name different horses | No guard bypass/reset; hypothesized input only with explicit acceptance | User explicitly approves41-hypothesis migration and direct API route | Accepted risk |

## Approved recovery decision (T3)

Expand operator-selected migration scope to the41 diagnostic candidates, not compiled presets and not a DB reset. Re-read current targets before use; candidate JSON is a receipt, not an executable or approved manifest. Use official source identities reported by race attempts plus canonical old horse data to prepare inputs. Keep existing max16-operation bound, stop/drain/backup, full typed-reference and collision checks, rollback/idempotency and result checks unchanged. Do not force unknown/ambiguous data through; report excluded/conflicting pairs. After verified migration, retry only eligible affected tasks; a race may expose another horse conflict after its first one is corrected, so do not promise41 mappings exhaust all latent failures. Collection resume remains a separate approved operation and requires actual successful replacement/progress evidence.

The4 ambiguous sire profiles require fresh candidate evidence/candidate-bound repair; the trainer requires current navigation reproduction and a bounded correction if still reproducible. Do not dismiss these5 or claim the whole incident resolved after the41 migration. Any new production-code/public-contract change still needs a concrete scoped design and code gate; no such write scope has been released.

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Current groups/targets/tasks/counts and persisted causes reconcile without guessing missing evidence | T1,T2 | GET group/detail/batch evidence plus exact source contract | Verified |
| AC2 | Each in-scope deterministic cause has an approved simple correction and regression evidence, not merely a retry | T2,T3,T4,T6,T7 | Real producer/persistence/consumer path and counterexample tests | Connected |
| AC3 | Corrected eligible work succeeds and collection progresses; history retained, no blanket reset/hidden failures | T4,T5,T7 | Actual migration/postchecks/replacement tasks and lane timestamps | Connected |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Capture live scoped API inventory and deduplicate targets | Lead | Lead | none | Authorized read-only production API and records | GET status/count/group/resource/history/batch evidence; no secrets/raw bodies | api-observation.md and saved sanitized receipts;46unique targets,399attempts,16batches,41diagnostics | Verified | Lead — credential privacy and production evidence acceptance | none | unavailable; retries 0; corrections 0; reviews 0 |
| T2 | Trace four observed failure codes and API contracts | pending_planner | Luna/high | none | Read-only collection contracts, handlers and identity validation | Exact routes/wrappers/fields, caller traces, source test locations | Source report independently corroborated by Lead resolver/DTO reads and actual API contracts | Verified | Worker — bounded repository investigation | T2-A1 | unavailable; retries 0; corrections 0; reviews 1 |
| T3 | Freeze corrective scope and data safety decisions | Lead | Lead | T1,T2 | This record and affected canonical design docs | Fact/hypothesis reconciliation, concerns, scoped acceptance and approval | User explicitly approved41-input migration/direct API route; C1 Accepted risk | Verified | Lead — architecture/data/approval decisions | none | unavailable; retries 0; corrections 0; reviews 0 |
| T4 | Verify migration transport and safe-continuation source contracts | continuation_contracts | Luna/high | T3 | Read-only migration endpoints, DTOs, supervisor and operational docs; subject candidate repair and trainer navigation contracts | Exact current method/path/payload and phase-before-write evidence, max16 bound, backup/postcheck/restart/retry; source only, no new code/tests | A2 verified actual6routes/bodyless execute/statusGET and current+old Applying→CLI→write; separate transaction/cleanup gap. Lead corroborated exact source; current cancellation eligibility not claimed | Verified | Worker — bounded existing-contract investigation | T4-A1,T4-A2 | unavailable; retries 0; corrections 0; reviews 2 |
| T5 | Execute approved migration and recover eligible targets | Lead | Lead | T3,T4,T7; approved child runtime restoration | Approved direct production API operations and records | Backup/safe-state/apply/postchecks/selected retry/separate resume/progress | Protected UI restored, f8 last observedStoppingApi;27Oct conditional cancellation approved but not implemented, no apply/postcheck/migration success | Dependent | Lead — credentials/persistence/data/final acceptance | none | unavailable; retries 0; corrections 0; reviews 0 |
| T6 | Repair remaining4 ambiguous profiles and trainer after evidence/design | Planned Luna investigator | Luna/high | T5 runtime availability; scoped approval before any new implementation | No code/prod write scope released; existing source contracts only | Fresh candidate identity/navigation evidence, appropriate regression and terminal recovery | Sire origins/persisted choices known; no fresh external candidate validation; trainer currentURL unavailable | Dependent | Worker — bounded source investigation; Lead identity decisions | none | unavailable; retries 0; corrections 0; reviews 0 |
| T7 | Diagnose blocked supervisor handoff and establish safe continuation | Lead | Lead | Approved19Oct cancellation implementation and verification; containment/bootstrap complete | Approved child cancellation implementation/deployment and conditional operation; no blind replay/reset/resume | Exact gate/journal/process proof; preserve original request/history, reject unknown states | UI restored/run40301038134 success.27Oct conditional cancellation approved; child journal/controller/UI/native proof pending. Original24Oct cause unproven | Dependent | Lead — operational authority/data/security decisions; frozen implementation delegated in child | none | unavailable; retries 0; corrections 0; reviews 1 |

## Documentation updates and review

Current architecture/behavior is not changed during read-only investigation. T3 will update affected canonical docs before any new design approval. Orchestration/DDD/incident skills loaded in this turn. Read-only T2 needs no code edit, test change or full suite; executable source queries and exact current contracts are its evidence. No new public contract or production code may be changed while Proposed. This record owns resolution, not just investigation; all unknown causes remain unfinished. Main preserves userdirty AGENTS/skills/policy tests and prior parent handoff.

Review gates24Oct execution: Lead design/task-split and concern review retain existing operation architecture, max16/generic inputs and C1 acceptance; only41candidate operational scope expands. Pre-implementation review is operational only: no code writes, T4 source read-only/Luna, T5 credentials/migration/acceptance Lead, verified actual DTOs and prior regression locations; no code/build replay required. Checkpoint AC2/AC3 review catches blocked-host503 and missing preapply continuation, not a successful migration. Final handoff keeps Approved with acceptance-blocking T5/T7, other5 T6 Dependent; no completion claim. Required next authority is server-side diagnosis outside the chosen API-only route, not a local AWS login or a new copy of the key.

Checkpoint/final diagnostic review24Oct: canonical audit validator exit0; sanitized JSON counts41/46, unique46tasks/resources and candidate identifier shapes PASS; git diff --check exit0. No product source changed, so build/regression is not claimed or replayed for this read-only receipt. T1/T2 source/API evidence independently reviewed; AC1 Verified, AC2/AC3 still incomplete. Secure credential disposed and process exited0. Next after explicit C1 decision: fresh candidate revalidation, existing generic migration admission/operation path with unchanged safety gates; separately scope remaining5 cause repairs. No background agent or continuing production operation is running while awaiting that decision. Unrelated dirty policy/AGENTS/routing/efficiency files remain outside this diagnostic checkpoint.

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | In progress | Approved containment/bootstrap child source committed; parent data operation and other5 corrective designs not complete |
| Verification | In progress | Diagnosis/transport confirmed; blocked supervisor handoff is a new unresolved operational failure |
| Deployment/operation | In progress | 41target reviews pass; first16 admitted/executed but blocked before apply proof; later25 not admitted, zero confirmed migrated |
