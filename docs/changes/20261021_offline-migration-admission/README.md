# 停止後の全件検証による馬ID移行

- Status: Proposed
- Change record schema: 2
- Owner: Lead
- Created: 2026-10-21
- Updated: 2026-10-21
- JRA site contract impact: None

## Context and scope

Parent: [admin-screen recovery](../20261017_admin-screen-recovery/README.md), F45/T26 and original3 approved hypothesis pairs. This proposal does not authorize Ecolotera, ambiguous same-name identities, bulk retries, resets, automatic resume or weaker data checks. Parent operational acceptance remains unfinished.

Actual03484dd0 cold/warm authenticated UI previews both stop at the30s HTTP deadline, during TypedReferences. Completed-page lower bounds:27167rows/1727548bytes and30533rows/3941480bytes. Last page does not identify the hotspot or total volume. Reader's existing whole-scan bounds are30min/1Mrows/4GiB. Native small fixtures are not production capacity evidence. No behavior-preserving optimization can guarantee unknown-volume full census in30s. No production data has been migrated.

## Hypothesis review

| Claim | Boundary and evidence | Falsifier/result | Disposition |
| --- | --- | --- | --- |
| HTTP deadline prevents current online admission | Fact: exact endpoint deadline branch and actual DeadlineExceeded metadata twice | Warm Lazy-cache falsifier still30010ms | Confirmed |
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
| C1 | Online full-data evidence will no longer precede saving/explicit Execute | Saving is pending only; full checks/real evidence still mandatory after API exit and beforewrite. Alternative retain blocked online gate; review shows pending clearly | AC1/2/3; P1/P2/P3; pending directwriter reject | Agree with this explicit gate relocation | Await approval of summarized proposal | Resolved in design |
| C2 | Pending/bound identity, duplicate requests and crashes can create a binding gap | Stable immutable manifest request identity, separate exactly-once sealed canonical evidence; ownership/gate hashes never silently mutate meaning. Old bound records preserve semantics; durable migration only additive/journal-compatible. Reject manifest/source/backup mismatch, concurrent bind and partial binding; no blind standalone method reuse | AC2/3/4; P2/P3; crash/duplicate/mixed-source tests | Agree; do not bypass fingerprint | Await approval | Resolved in design |
| C3 | Site downtime and unknown data/volume may still prevent migration | Retain900s drain and finite fullscan budgets; backup beforewrite; fail closed with useful status. No guarantee all unknown data is migratable; do not force/reset. Retry/resume separate | AC3/4/6; P3/P5; large/unknown/cancel/partial tests | Agree with preserved stop conditions | Await approval | Resolved in design |
| C4 | Changed cross-process path invalidates happy-path coverage of old lifecycle alone | One new real pending-mode900s lifecycle plus bounded crash/kill/ownership tests, whole CI and actual3pair receipts. Reuse old unchanged-path tests without claiming they prove new pending path | AC4/5/6; P4/P5 | Agree; this adds necessary verification time | Await approval | Resolved in design |

No unresolved agent objection. Production throughput, total volume and extra Ecolotera identity remain unknown, not silently approved assumptions. Partial migration never clears the maintenance gate or resumes collection. Restore/reset after partial failure requires exact-target evidence and fresh authority.

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Logged-in operator can review exact approved mappings and save pending request without full online census; UI clearly says unvalidated; no write or auto run | P1,P2,P4 | Real HTTP/client+bUnit+390px/keyboard/loading/error/duplicate; malformed/ambiguous/unknown blocked | Not started |
| AC2 | Pending identity is immutable/idempotent and canonical evidence is sealed exactly once to same manifest/source set; no fabricated fingerprint or direct unbound write; old bound journal records remain usable | P2,P3,P4 | SQL read-back/roundtrip, same/different key, concurrent/crash binding, old fixture migration, caller contract tests | Not started |
| AC3 | Existing authorized supervisor stops/drains normal API, validates backup/capacity, completes all configured-data census before firstwrite, retains collisions/unknown data/cancel/row/byte/time limits and writer freshness | P3,P4 | Independent beforewrite invariant/counterexamples and full all-store tests, held API/failed backup cannot apply | Not started |
| AC4 | Full data preserved except approved identity changes; restore evidence retained; partial/kill/timeouts remain blocked/no replay/no auto resume; successful restart paused | P2,P3,P4 | Typed/global hashes, backup/postcheck/replay tests, real lifecycle/crash controls | Not started |
| AC5 | Final source matches actual tested input, exact formatter/build/fullCI and original deploy gates pass | P4,P5 | Native complete commands +matching-SHA CI/deploy/annotations; no skipped mandatory branch | Not started |
| AC6 | Actual original3 approved pairs migrate successfully with backup and postchecks; selected replacement jobs succeed, then separate resume demonstrates three-lane progress | P5 | Actual UI GUID/status/receipt, failure history and lane timestamps; no forced fourthpair/reset. An external blocker leaves this AC and recovery unfinished | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| P1 | Freeze exact contract/task split after user approval | Lead | Strong Planner — architecture/security/data acceptance, no ordinary coding | Approval | This record; no source | Canonical audit/DDD and consumer inventory | Proposed above; do not dispatch coding yet | Proposed | Lead — architecture/data/security/final acceptance | none | unavailable; retries 0; corrections 0; reviews 0 |
| P2 | Pending DTO/journal/endpoint/UI with atomic binding invariants | pending-admission executor | Luna/high — ordinary scoped implementation | P1 | Shared Identity contracts; migration journal/policy/endpoints/client/admin page; matching tests. Serialize shared-file ownership with P3 | Focused transport/journal/UI tests then handoff hashes | Not started; exact file list fixed before dispatch | Proposed | Worker — frozen bounded implementation/verification after approval | none | unavailable; retries 0; corrections 0; reviews 0 |
| P3 | Offline full validation and existing supervisor/writer integration | offline-execution executor | Luna/high — frozen lifecycle implementation | P2 | Migration supervisor/runtime/ownership/engine as needed; matching tests. No overlap with active P2 | Beforewrite/backup/oldformat/kill/timeout/partial tests | Not started; material gate changes return Lead | Proposed | Worker — frozen bounded implementation/verification after approval | none | unavailable; retries 0; corrections 0; reviews 0 |
| P4 | Independent integrated counterexamples/native gates | lifecycle verifier | Luna/high — mechanical verification and local corrections | P3 | Relevant tests plus disposable isolated environment only; source corrections return exclusive executor | Exact CI format/build/test, real pending900s plus crash, full consumer roundtrip | Not started; no runtime/model/usage inferred | Proposed | Worker — frozen bounded implementation/verification after approval | none | unavailable; retries 0; corrections 0; reviews 0 |
| P5 | Deploy, execute approved3, verify recovery | Lead | Lead — production/data/security/final acceptance | P4 | Canonical GitHub deploy/admin UI and governing records only | Actual matching-SHA gates/backup/apply/postchecks/selectedretry/separateresume/3lanes | Not started; AWS login not required | Proposed | Lead — architecture/data/security/final acceptance | none | unavailable; retries 0; corrections 0; reviews 0 |

Delegation uses selectable Luna/high; requested and observed model/usage recorded separately at dispatch. No cost or speed claim. Exact test commands and file ownership freeze in preimplementation plan; no coding until approval/canonical audit. Continue independent already-approved T25 CI correction while this proposal awaits approval.

## Review gates / documentation

Design/task split and concern review: Lead/source plus read-only T26 evidence; pending is not full preview; fingerprint and binding safeguards explicitly retained at offline boundary. No new permanent service or speculative optimization. Approval is for all six ACs and C1–C4 recommended dispositions only.

Preimplementation, checkpoint and final gates not yet run. Parent and original hypothesis records must be reconciled with deployment/operational evidence; neither can become Implemented merely from this proposal or code completion.

Documentation updates: `docs/20-admin-ui-design.md` now links the deployed recovery contract and labels this delta explicitly proposed/unapproved, preserving the current admission guard. Inspected recovery/operator materials found no separate canonical migration runbook. This record is the pending-admission proposal, not a claim that it is deployed; reconcile parent scope and operator instructions on implementation.

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | Design approval required; no pending-mode production code changed |
| Verification | Not started | Source/actual timeout evidence only, not new-path proof |
| Deployment/operation | Not started | Parent live recovery incomplete; collection paused |
