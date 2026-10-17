# 管理画面からの馬ID移行・復旧

- Status: Proposed
- Change record schema: 2
- Orchestration schema: 2
- Owner: Lead
- Created: 2026-10-17
- Updated: 2026-10-17

## CURRENT

- User requests recovery entirely inside admin UI; explicitly rejected separate GitHub Actions screen handoff on2026-10-17.
- Existing code/production evidence investigated; no production code edited. Previous deployment5b343cd7 succeeded, migration not executed.
- Lead owns architecture/security/approval. Requestedgpt-6-luna/high readonly planner completed; runtime model/tokens unavailable.
- Next: user approval of this design, including initial scoped GitHub credential and expected API downtime; then serial diagnostic/backend/UI execution.
- Existing UnknownShape cause remains unverified; new diagnostic must establish actual catalog difference before any lineage correction/apply.
- Preserve all dirty AGENTS/skills/policy-test/routing/efficiency files. No AWS login prerequisite for delivery through canonical GitHub deployment.

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | New public workflow/credential boundary requires design approval. |
| Verification | Not started | Readonly source/production investigation complete; implementation gates not run. |
| Deployment/operation | Not started | Diagnostic deployment, actual schema closure, scoped credential setup, migration and real job progression remain. |

## Context and evidence

Parent [approved migration](../20261014_horse-id-hypothesis-migration/README.md) remains unfinished; this change does not remove its data-preservation, offline CLI, backup, postcheck or production-recovery criteria. It proposes replacing its fixed-three-candidate/manual-GitHub-screen boundary, not its physical migration engine.

- Real signed preview: EventStore Observed22objects; Collection UnknownShape26known/0unknown; Schedule Observed1; LocalQueue NotConfigured;0rows/bytes/pages. Collection has25 expected tables plus history; unknown history or a complete table/index/constraint mismatch remains possible. Fresh-EF local fixture passed; production lineage is not established.
- New Niigata1 failure: DomainWriteRejected/HorseIdentityConflict エコロテラ, JRA2024104541, attempt8/latest2026-10-17 10:03:30JST. Currenthorse-bf2eb275-9b82-51a0-b7f5-57be4c4c1ded differs from actual-helper canonicalhorse-99baa696-62db-5bf6-b4f1-eae5d936ec64; no DOB/profile/source evidence. Canonical diagnostic returnsnotfound. This is not proof that both identifiers represent one horse.
- Existing UI hardcodes3 candidates. Preview/stage/status APIs accept bounded manifests but staging creates a fresh server GUID on each request. Journal workflow_claimed is an offline execution claim, not a durable GitHub dispatch intent. No persistent app dispatch credential exists.
- Canonical workflow app-deploy.yml already accepts only horse_id_migration_operation_id, supplies AWS OIDC/SSH credentials, performs pause/drain, backup, API stop, fixedCLI, restart and integrity checks. Normal API must not execute physical writes concurrently.
- [GitHub REST workflow dispatch](https://docs.github.com/en/rest/actions/workflows#create-a-workflow-dispatch-event), verified2026-10-17: API version2026-03-10 returns200 with workflow_run_id and URLs; fine-grained token requires repository Actions(write). Successful response enables direct run-ID persistence; a lost response is still an uncertain external outcome.

Hypotheses and falsification:

| Claim | Fact/inference and supporting/contradicting evidence | Smallest check/result | Disposition |
| --- | --- | --- | --- |
| UI cannot diagnose exact UnknownShape today | Source discards reason into enum; real preview shows only counts | InspectSchemaAsync and actual preview agree | Confirmed; add bounded structured diagnostics, not guard relaxation |
| Current production schema can be migrated unchanged | Unknown; current fixture pass does not prove production lineage | New readonly report, source-produced representative fixture and original classifier/writer tests | Owned unresolved investigation T2/T6; no migration-complete premise |
| Ecolotera pair identifies one horse | Unsupported without provenance; same name and missing DOB/source do not prove it | Existing current/registration/profile diagnostics + official source evidence | Keep unselected hypothesis; no automatic merge or concrete-pair approval here |
| App can run existing deployment without local AWS | Workflow credentials already supply canonical route; app lacks trigger credential | Real deployment38858413331 succeeded; official dispatch contract | Add explicit dedicated credential, not registry-token reuse |

## Goals and non-goals

管理画面で「診断 → 候補確認 → プレビュー → 実行確認 → 実行状況/結果 → 対象の再取得 → 明示的な再開」を完結する。通常運用でAWS/GitHub画面やCLIを操作する必要をなくす。

初回の専用GitHub認証設定は必要であり、この例外を隠さない。診断で未知データが残れば実行不可と表示する。未対応DBを自動で修復できると約束しない。DBリセット、任意SQL/シェル/パス入力、API内物理移行、Docker socket、新たなAWS権限、別の移行worker、名前一致の自動統合、停止/部分移行の自動リトライは追加しない。

## Experience and interaction design

Existing `/settings/horse-id-migration` を段階表示の独立Pageとして拡張。障害/馬診断から移動できる。初見の利用者が次の操作を判断できるよう、各段階のPrimary Actionは一つとし、技術詳細は展開表示する。設計モックは [mocks/recovery.md](mocks/recovery.md)。

1. **保存先を診断**: 明示操作でreadonly診断。保存先、観測時刻、対応状態、不一致の種類、許可された構造情報と次の行動を表示。生SQL、DBパス、業務行、例外全文は表示しない。レポートを安全なJSONとしてコピー可能。未知のSQL構文/識別子は分類とfingerprintに留める。
2. **移行候補を確認**: 既存3件は未選択の参考候補として維持。登録馬を検索/選択し、JRA馬番号10桁を入力して最大16件まで追加できる。旧ID、現登録/初回登録、DOB、source証拠と不足、サーバーで導出した新IDを表示。証拠不足は仮説として明示確認し、同名を自動選択しない。行追加/削除/変更時は古いpreview/確認を破棄する。手入力先は馬番号のみで、任意URL/SQL/新IDは受け付けない。
3. **プレビュー/実行確認**: 共通policy、全設定保存先、対象root/typed references/collision、fingerprintを使用。Unknown/Partial/未確認・未対応があればstage/dispatch不可。既存のpause状態を確認してから実行。明示確認後にserver GUID依頼を保存し、別の「移行を実行」操作でその固定GUIDだけをdispatchする。保存だけでは実行しない。
4. **実行と再接続**: 送信中/受付済み/受付確認待ちと、実際の移行Running/検証済み成功/失敗/Partial/Interruptedを区別。GitHub run成功だけでは移行成功にしない。GUIDをURLへ残す。API停止中の接続断を事前説明し、画面再読込/再ログイン後も同一依頼へ戻る。手動の「状態を更新」でworkflow状態とjournalを確認し、隠れた自動dispatch/pollはしない。
5. **復旧**: 移行成功receiptと必要postchecksが揃って初めて既存の障害画面へ誘導。現在の正確な通知IDを確認して再取得を依頼し、pipeline再開は別の明示操作。新taskの成功と3レーンの実進行を確認するまで「復旧完了」にしない。

PCでは完全IDを折返し表示、390pxでは候補/診断情報を縦積み。Fluent componentを優先。Loadingは未取得を0件と表示せず、失敗時は入力/現在状態を保持し、再操作可能な文言を出す。ラベル、inline validation、keyboard/focus、二重送信抑止をbUnit/実browserで検証する。候補追加からプレビューまで画面遷移不要。実行確認後に外部画面の操作を要求しない。

## Technical decisions

### Diagnostics and schema correction

既存readonly schema readerにclosed reason codesとbounded expected/observed descriptorsを追加する。table/column/index名は既存ソースのallowlistのみ、unknown名/SQLは件数・種類・hashに留める。履歴versionは整数列として表示。最大512objects、SQL検査8KiB/object、names512bytes、report1MiB、30秒/明示requestを上限とし、超過はIncompleteで実行不可。sourceデータはreadonly接続で読み、通常storeのconstructor/auto migrationは呼ばない。これ自体はschema履歴や業務データを変更しない。

診断の配備後、実DBの差異を確認してsupported cohortの正確なfixtureを用意する。必要な修正は、従来の保存内容を変更せず認識可能にするか、明示的なbounded data migrationで保全する。unknown schema/historyを単に許可する修正は禁止。実データ消失や既承認の保存要件変更が必要なら再承認する。

### Durable request/dispatch

Stage retryではcaller idempotency keyとimmutable request hashをjournalで対応付け、同key/same内容は同server GUID、異内容は409。caller keyはoperation GUIDではない。requestKeyをURLに保存し、response消失時はjournal-only lookupからGUIDを回収できる。journal既存行/receiptと64MiB等の容量・保全上限を維持する。

Dispatchにはoffline workflow_claimedとは別のdurable intent/attempt/time/runID/outcomeを保存する。ネットワーク呼出し前にsingle-owner intentを確定し、二重クリック/複数instance/API再起動でも無条件再送しない。固定github.comの対象repo `chameleonhead/HorseRacingPrediction`、workflow `app-deploy.yml`、ref `main`、既存GUID inputだけに限定。利用者からrepo/ref/workflow/command/URLを受け取らず、redirectを追わない。GitHub2026-03-10の200 responseをbounded検証しrunIDを保存する。未知/旧形式/不正responseやtimeoutは確認待ちで、成功/未実行と推測しない。

状態照会は認証済みの固定run lookup、およびGUIDを含むrun-nameによるbounded照合を使用し、業務ログは取得しない。remote retryは勝手に行わない。受理不明で照会しても確定しない場合は、同GUIDを使った明示的な再送確認だけを許可し、最大3attempts、直近attemptから2分以上、Running/Partial/Interrupted/Succeededなら禁止する。重複workflowが作成される可能性は表示し、既存のconcurrency/claim/OS lockとSucceeded receipt-only replayで物理移行の二重適用を防ぐ。exactly-once HTTP送信を保証するとは記載しない。

HTTP外部呼出しは20秒total、response1MiB以内、pagination最大3pages×100件、照会対象をworkflow/GUID/開始時刻で絞る。body/header/token/例外全文をログへ出さずsafe reason/statusのみ。workflow failureとdomain migration failureを別表示する。blocked minimal hostではjournal-only status/request lookupだけを維持し、source診断/dispatchは禁止する。既存CLI `horse-id-migration <GUID>` の契約は変更しない。

### Credential and access boundary

専用fine-grained PATを対象repoだけ、Actions(read/write)、有効期限30日以内で利用者が初回作成し、GitHub Actions secret `HORSE_ID_MIGRATION_ACTIONS_TOKEN` に設定する。tokenはrepo内の他workflowにも権限を持ち得る（GitHub側でworkflow単位に制限できるという主張はしない）。アプリadapterは上記固定workflowのみ許可する。registry token、既存CLIの広い個人token、workflow-scoped GITHUB_TOKENを流用しない。

既存deploy secret→SSH env→chmod600の.env→compose/server config経路を拡張する。値をチャット、ソース、URL、出力、ログ、公開DTOへ残さず、画面には設定済/未設定/期限・権限エラーのみを表示。未設定ならdiagnostic/previewは利用でき、実行だけ不可。期限切れは明示停止し無限retryしない。設定/権限付与は利用者の明示操作を要する。新しいAWS権限は付与しない。通常UIは既存cookie/antiforgery、server側AdminApiClient→API-key endpointの境界を維持する。

## Finite route ledger

全12routes: preserved6、extended2、new4。UIは既存authorized migration pageを拡張。既存horses/diagnostics/recovery/resumeを別の汎用管理APIへ統合しない。

| Method/path | Disposition | Consumer and preserved invariant |
| --- | --- | --- |
| POST /api/v2/admin/horse-id-migration-previews | extended | migration UI/client; safe diagnostics fields追加、readonly/fingerprint/blockers維持 |
| POST /api/v2/admin/horse-id-migration-operations | extended | migration UI/client; idempotency key対応、serverGUID/immutable request/journal-only stage維持 |
| GET /api/v2/admin/horse-id-migration-operations/{operationId} | preserved | normal/minimal UI; journal-only safe receipt、source/externalcallなし |
| GET /api/v2/admin/horse-id-migration-schema-diagnostics | new | candidate不要のmanual readonly診断; key-first auth/limits、source書込なし、minimalhostでは503 |
| GET /api/v2/admin/horse-id-migration-requests/{requestKey} | new | response消失後のrequest lookup; key-first auth/journal-only、serverGUIDへ復帰 |
| POST /api/v2/admin/horse-id-migration-operations/{operationId}/dispatch | new | explicit run/re-send; durable sameGUID/fixedworkflow、許可済stageのみ |
| GET /api/v2/admin/horse-id-migration-operations/{operationId}/execution | new | manual status; bounded authenticated GitHub lookup、safe DTO、domain successと区別 |
| GET /api/v2/admin/identity-diagnostics/horses/{horseId} | preserved | candidate evidence; source/time limits、no identity proof inference |
| GET /api/horses | preserved | horse picker/name search; API key、pagination、name matchはidentity proofでない |
| POST /api/v2/admin/collection/recovery-batches | preserved | existing failure UI; exact notifications/revision再検証、失敗task書換えなし |
| PUT /api/v2/admin/collection/pipeline | preserved | existing Jobs UI; explicit separate resume、移行receiptだけで自動再開しない |
| GET /api/v2/admin/collection/operations/progress | preserved | existing runtime UI; real timestamps/lane progressを観測、読込zeroを実績としない |

Standalone schema診断は候補不要の専用GETを使用する。空manifestや多態的なdiagnostic-only POST modeを導入せず、既存previewのrequest wrapper互換を維持する。GETとpreviewは同一readonly classifier/reason mapperを共有し、診断に成功しただけでstage可能とは判定しない。

## Concern and agreement ledger

| ID | Concern/evidence | Impact | Proposed disposition/alternative/residual risk | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | App currently lacks Actions credential; fully internal execution requires new authority | App compromise can trigger repo workflows | Explicit dedicated repo-scoped expiring token, fixed adapter, no generic dispatch; alternative externalGitHubUI rejected by user. Rotation needed | AC3/T3 auth/config/redaction | Support with explicit grant | App-only requested; token grant pending design approval | Resolved in design |
| C2 | API must stop during physical migration | Temporary screen disconnection | Preserve offlineCLI; durable URL/GUID/manual reconnect/status, no parallel API writer. Alternative in-process rewrite rejected | AC3,AC4/T3,T4 restart | Support safety boundary | Pending | Resolved in design |
| C3 | HTTP accepted-but-lost response and stage retry ambiguity | Duplicate requests/workflows or false success | Stable stage key; durable dispatch intent, response runID, no automatic retry; explicit bounded sameGUID re-send may queue duplicates but cannot doubleapply | AC3,AC4/T3 interruption | Support; no exactly-once claim | Pending | Resolved in design |
| C4 | DBUnknownShape and missing identity provenance remain real unknowns | Unsafe migration/incorrect merge | Diagnose actual schema first; explicit unselected hypotheses, complete typed preflight; forbid reset/merge/guard bypass. No promise automatic repair of arbitrary DB | AC1,AC2,AC5/T2,T6 | Objection to forced repair | Pending safety-boundary approval | Resolved in design |
| C5 | Adding screens alone does not recover original jobs | False completion | Deliver diagnostic→schema closure→safe migration→selected retry→separate resume→actual lane progress; keep parent tasks incomplete until evidence | AC5/T6 | Support end-to-end gate | Pending | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Logged-in operator can diagnose exact bounded schema mismatch/next step from UI without AWS/SQL; no business rows/paths/raw SQL/secrets; incomplete schema remains blocked | T0,T1,T2,T4,T5,T6 | Real fixture history/column/index/syntax/cap counterexamples; auth-before-read; actual production diagnostic | Not started |
| AC2 | Operator can add up to16 existing-horse/JRA-number pairs, review evidence/canonical ID, explicitly accept hypotheses and preview; no name-auto-match, target merge or arbitrary newID/URL input | T0,T2,T4,T5 | bUnit validation/selection/stale reset; real transport invalid/duplicate/source collision; longIDs/390px/keyboard | Not started |
| AC3 | After one-time dedicated credential setup, UI dispatches only fixed savedGUID through canonical workflow, with durable idempotence/unknown-state handling; no AWS or GitHub screen required for ongoing execution | T0,T3,T4,T5,T6 | MockHTTP fixed payload/200runID/redirect/timeout/auth/rate/token error; real journal process restart/multiinstance/doubleclick; matching deployment guard | Not started |
| AC4 | Same request is recoverable after response loss/API stop/reload; workflow and domain statuses remain distinct, partial/interrupted cannot be resumed or shown complete; explicit status/re-send controls are bounded | T0,T3,T4,T5,T6 | Loststage response, killeddispatch process, acceptedresponse-loss, duplicate workflow/receipt-only replay, minimalhost isolation, browser reconnect | Not started |
| AC5 | Actual production schema corrected safely or precisely blocked; all configured data preserved or verified migration available; after approved identity mapping/postchecks, selected replacement jobs succeed and three lanes visibly progress following separate resume | T1,T2,T3,T4,T5,T6 | Source-produced actual-cohort fixture/full preservation; real backup/apply/postrestart/selected retry/resume/progress receipts; no reset without fresh approval | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T0 | Existing contract/config/source reconnaissance | screen_recovery_planner | Luna worker | - | Readonly repository investigation | Exact source route/config/journal/doc citations plus independent official contract | Source findings adopted; canonical UI doc independently read; no code/tests changed | Verified | Worker — bounded readonly cheap planning | T0-A1 | unavailable; retries 0; corrections 0; reviews 1 |
| T1 | Freeze design/security and operational sequence | Lead | Lead | T0 | This record; docs/20-admin-ui-design.md | AC/concern/route/task reconciliation and explicit approval | User chose app-only; detailed design approval pending | Proposed | Lead — architecture/security/public contract | none | unavailable; retries 0; corrections 0; reviews 0 |
| T2 | Diagnostic reasons and editable manifest backend | Diagnostic executor | Luna worker | T1 | src/HorseRacingPrediction.Api/IdentityMigration/TypedHorseIdentityStoreReaders.cs; src/HorseRacingPrediction.Api/IdentityMigration/TypedHorseIdentityStoreEngine.cs; src/HorseRacingPrediction.Api/Endpoints/IdentityMigration/HorseIdentityMigrationEndpoints.cs; src/HorseRacingPrediction.Contracts/Identity; tests/HorseRacingPrediction.Api.Tests/HorseIdentityMigrationStoreEngineTests.cs; tests/HorseRacingPrediction.Api.Tests/HorseIdentityMigrationPreviewTests.cs; tests/HorseRacingPrediction.Api.Tests/HorseIdentityMigrationEndpointsTests.cs | Focused reason/limits/auth/preview tests then related regression | Not started; exact file additions frozen before dispatch | Proposed | Worker — source-backed classifier/transport, serial ownership | none | unavailable; retries 0; corrections 0; reviews 0 |
| T3 | Durable dispatch adapter/journal/credential deployment | Dispatch executor | Luna worker | T1,T2 | src/HorseRacingPrediction.Api/IdentityMigration; src/HorseRacingPrediction.Api/Endpoints/IdentityMigration; src/HorseRacingPrediction.Contracts/Identity; src/HorseRacingPrediction.Api/Program.cs; src/HorseRacingPrediction.Api/Web/ApiBrowsing/AdminApiClient.HorseIdentityMigration.cs; .github/workflows/app-deploy.yml; deploy/docker-compose.yml; tests/HorseRacingPrediction.Api.Tests/HorseIdentityMigrationJournalTests.cs; tests/HorseRacingPrediction.Api.Tests/HorseIdentityMigrationEndpointsTests.cs; tests/HorseRacingPrediction.Api.Tests/HorseIdentityMigrationClientTests.cs; tests/scripts/test-deploy-migration-guard.ps1 | MockHTTP/real journal restart/oldjournal migration/guard tests plus exact formatter | Not started; source-backed exact file plan before first edit | Proposed | Worker — serial journal/transport implementation; Lead credential decision | none | unavailable; retries 0; corrections 0; reviews 0 |
| T4 | Guided recovery UI and component/browser tests | UI executor | Luna worker | T2,T3 | src/HorseRacingPrediction.Api/Web/Components/Pages/HorseIdentityMigrationAdmin.razor; src/HorseRacingPrediction.Api/Web/Components/Pages/HorseIdentityMigrationAdmin.razor.css; src/HorseRacingPrediction.Api/Web/Components/Pages/CollectionFailureGroupDetail.razor; tests/HorseRacingPrediction.Api.Tests/HorseIdentityMigrationAdminComponentTests.cs; tests/HorseRacingPrediction.Api.Tests/HorseIdentityMigrationAdminBrowserTests.cs | bUnit plus actual browser desktop390px/keyboard/loading/error/unknown/reconnect | Not started | Proposed | Worker — existing Fluent patterns, no generic recovery framework | none | unavailable; retries 0; corrections 0; reviews 0 |
| T5 | Integrated regression and matching CI | Verifier | Luna worker | T2,T3,T4 | Temporary verification logs only | Exact workflow commands, real runner parsers, full tests/formatter/graph/diff | Not started | Proposed | Worker — mechanical verifier; Lead independent AC-group acceptance | none | unavailable; retries 0; corrections 0; reviews 0 |
| T6 | Diagnostic delivery, actual cohort closure, credential setup and production recovery | Lead with bounded cohort executor | Lead / Luna worker | T2,T3,T4,T5 | Records/authorized operations; cohort code scope only after actual diagnostic plan | Matching GitHub terminals/annotations, actual schema fixture/preservation/postchecks/replacement success/lane progress | Not started; no concrete new identity pair or credential value approved by this proposal | Proposed | Lead — migration/security/production acceptance; ordinary cohort tests/edits delegated | none | unavailable; retries 0; corrections 0; reviews 0 |

Routing preference is gpt-6-luna/high; selectable high-capable Luna fallback if unavailable. Requested versus observed model and usage remain distinct. All production writers serialized; source changes require exact per-dispatch scope/readiness/audit, focused tests during implementation and related regression before handoff. Do not run broad regressions for every prose edit. Source-dependent graph sync and final exact formatter required. Same-cause failures trigger actual consumer inspection, not repeated guesses.

## Delivery and review gates

1. Design/task-split and concern review: Lead reviewed source facts, finite routes, offline boundaries, credential lifecycle, interruption/duplicates and data loss. Proposed additive design; no app code changed. Explicit app-only choice does not by itself approve this credential/public-contract design.
2. After approval, deliver diagnostics/backend first and deploy through existing GitHub route; inspect actual schema from screen. Independently continue only approved source slices. Actual cohort correction receives a bounded source-backed plan/tests, not a broad schema-acceptance bypass.
3. Credential bootstrap is separate explicit user action. Configure before accepting UI dispatch. Missing secret blocks only execution, not code/diagnostic progress. No localAWS prerequisite and no secret in chat.
4. Final integration tests/current CI, deploy newversion, actual read-only census, explicit candidate approval, preview/stage/screen dispatch, backed-up offline migration, postchecks, selected recovery, separate resume, observed lanes. Any Partial/unknown contradiction stops data operations.
5. Final review reconciles this and parent record/task/AC status; full workflow submission or code tests alone never close the incident.

## Documentation updates

- `docs/20-admin-ui-design.md`: add clearly Proposed successor link now; after approval reconcile the old manual-GitHub boundary and describe guided screen dispatch/initial secret/downtime. This remains the canonical admin UI document.
- Parent change record remains the canonical migration-engine/safety/actual production incident evidence. Its pending T20/T5/T26/T27 are not excluded by this new record; closure evidence will update them in the same work cycle.
- No other canonical migration runbook found by readonly docs audit. New mock is change-specific, not a competing operational source.

## Verification record

- Readonly Luna planner completed; no tracked source edits or tests performed. Main independently read canonical UI boundary and official GitHub dispatch contract.
- `python scripts/audit_agent_execution.py` passes for this and parent record; DDD validator reports issues=0; `git diff --check` passes (existing line-ending notices only). No implementation/credential/deployment/recovery claimed.
- Lead self-check: explicit app-only preference respected; initial credential/downtime are surfaced, finite12routes balance, normal/minimal host separation and uncertain outcome/re-send safeguards are specified, actual schema hypothesis remains an owned investigation. Standalone bounded GET chosen over an overloaded empty-manifest preview mode. Implementation remains gated on user approval, not localAWS access.
