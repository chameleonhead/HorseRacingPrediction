# 書き換え開始前の移行準備を画面から中止する

- Status: Proposed
- Change record schema: 2
- Owner: Lead
- Created: 2026-10-19
- Updated: 2026-10-26
- JRA site contract impact: None

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | 26Oct現行契約へ企画更新済み。利用者承認待ち。プロダクションコードは未変更 |
| Verification | Not started | 下記の取消・競合・再起動・拒否・UI検証 |
| Deployment/operation | Not started | 親の配備・本番復旧に統合。完了を親から除外しない |

## Context and goals

[親の管理画面内復旧](../20261017_admin-screen-recovery/README.md)の実行制御を調査した結果、maintenance gate作成後・Applying記録前の異常終了では、業務データを書き換えていなくても最小画面に留まる。親の承認済み最小画面はSucceededの明示readonly再検証しか変更を許さず、この準備段階から画面だけで戻る経路がない。

書き換え未開始を厳密に証明できた場合に限り、利用者の明示操作で準備を中止し、収集停止のまま通常画面へ戻す。元の依頼は失敗記録として残し、既存バックアップは保持する。再試行は新しいstage/GUIDで行う。同じGUIDのApply再開、バックアップ上書き、未知状態の解除は追加しない。

## Decisions and safety boundary

- 26Oct改訂。現在の保護用画面復旧は[限定配備](../20261025_operator-ui-bootstrap/README.md)で完了したが、本提案は未承認。旧案のexecute mode追加・存在しないexecution GET・全12routesという前提を下記で置き換える。
- Applyingは固定CLI起動前に永続化するwrite-ahead境界。現行と旧2872aaf双方の順序を確認済み。ただしStoppingApiだけではprocess終了・gate設置・現在の排他を証明しない。元の24Oct障害原因・実DB履歴は未確定のまま扱う。
- 対象は既知のAwaitingActionsかつStoppingApi/ApiExited/BackedUpの実行intent。Applying以後、Partial、一般的なFailed、不明・破損・別sourceは拒否する。特定ID・件数を決め打ちしない。
- executeはbodylessのまま変更しない。取消は専用bodyless POSTで要求し、既存operation GETで状態確認する。API要求で直接process停止・gate削除をせず、既存PID1 controllerへ渡す。
- 既存phaseのordinalを変えずCancellationRequestedとPreparationCancelledを追加する。受付はjournal transaction内でpreApplyingと既存要求を確認して記録し、Applyingへの遷移も同じtransaction境界で取消要求と競合させる。先に成立した一方だけが進む。
- controllerが書込可能な通常API/CLIの全終了、正確なsource-set/gate一致、全sourceのexclusive control/runtime leases、persisted pauseとdrainを再確認する。journal-only保護画面は維持でき、業務storeやCLIへアクセスしない。不明・不足は保護画面とgateを維持する。
- control lease下でintent PreparationCancelledとoperation Failed/安全な準備中止理由を1つのjournal transactionで確定する。その後に一致するgateだけを解除する。失敗状態一般にcleanup権限を与えない。再起動時は同じ確定receiptから解除完了だけを再確認し、Applyを起動しない。
- 重複・応答消失後の取消は同じreceiptを返す。二重解除・二重API起動はしない。正常APIの起動はSupervisorだけが行い、収集は停止を維持する。
- 保護専用配備の強制flagは通常復帰を妨げるため、既存workflow inputの対象GUIDを起動設定にもbindする。明示取消の確定receiptがそのGUID/sourceに一致し、全gate解除と通常startup guardがReadyの場合だけ正常childへ切替える。flag単独・GUID欠落/別GUID・未知状態では解除しない。次の保護配備は新しい対象GUIDにbindし、過去の取消receiptを流用しない。追加token・外部service・Docker socketは不要。
- 業務DBのschema/内容、backupの削除・上書き、自動取消、自動stage/Apply、同GUID再Apply、部分移行rollbackは対象外。journal履歴・制御状態のみ更新する。既存journal保持を基本とし、必要な運用metadata追加は旧schemaからの保存可能な移行を必須とする。
- 可否・遷移・拒否理由は値を入力する単体テスト可能なpolicyとし、process/file/lease I/Oを分離する。新しい汎用command frameworkや個別ID用の互換分岐は作らない。

承認依頼の範囲は実装・配備・今回の`f8f857b4-23c6-40d7-ac04-60676f42aa6d`の安全確認後の明示中止まで。条件不成立なら操作しない。既存workflow入力GUIDを新しい内部起動設定`HRP_HORSE_IDENTITY_OPERATOR_OPERATION_ID`へ渡し、既存boolean flagだけから解除対象を推測しない。固定IDをコードへ埋め込まない。新規stage/移行/収集再開をこの取消要求から自動実行せず、親の承認・検証範囲の別操作へ接続する。

## 26Oct source evidence and hypothesis boundary

現行endpoint mapは`HorseIdentityMigrationEndpoints.cs:22–29`の6routes。executeにはrequest DTO/modeがなく、BoundV1/PendingV2はstage種別。operation GETは`/{operationId}`であり`/execution`ではない。現行Supervisor588–596と旧2872aaf Supervisor215–221はApplying永続化→CLI認可/起動の順。writer196–203の業務更新は後続。Leadが実sourceを再読した。既存RecordFailureとAdvanceIntentは別transactionであり、既存cleanupはSucceeded+Verified専用なので、そのまま組み合わせて取消実装済みとは扱わない。

本番f8のphase3は候補判定の材料だけ。process/gate/lease/pauseと未開始証明は取消実行時に取得する。証明できなければ取消せず理由を残す。この条件付き動作を承認対象とし、現在取消可能・移行済みとは主張しない。

## Finite public-contract ledger

| Method/path | Disposition | Invariant |
| --- | --- | --- |
| POST /api/v2/admin/horse-id-migration-previews | Unchanged | 通常APIだけ、入力/preview契約維持 |
| GET /api/v2/admin/horse-id-migration-schema-diagnostics | Unchanged | 通常APIだけ、readonly診断維持 |
| POST /api/v2/admin/horse-id-migration-operations | Unchanged | BoundV1/PendingV2、stageとapplyを分離 |
| POST /api/v2/admin/horse-id-migration-operations/{operationId}/execute | Unchanged | body/mode追加なし、取消をexecuteに混在させない |
| GET /api/v2/admin/horse-id-migration-operations/{operationId} | Extended values | response shape維持、新phase/安全理由を表示、API-key認証 |
| GET /api/v2/admin/horse-id-migration-requests/{requestKey} | Extended values | 同じ保存operation/新phaseを返す、既存receipt維持 |
| POST /api/v2/admin/horse-id-migration-operations/{operationId}/verify | Unchanged | 既存readonly再検証の認証/CSRF/適格性維持、protected-onlyでは無効のまま |
| GET /settings/horse-id-migration | Extended UI | cookie認証、取消要求/確認中/拒否/完了を既存詳細へ表示 |
| POST /settings/horse-id-migration/{operationId:guid}/verify | Unchanged | 既存cookie/CSRFとreadonly検証、取消へ流用しない |
| POST /api/v2/admin/horse-id-migration-operations/{operationId}/cancel-preparation | New | bodyなし/API-key認証、同GUID idempotent、202 receipt/404/409/503安全理由、通常/最小hostで同じhandler |
| POST /settings/horse-id-migration/{operationId:guid}/cancel-preparation | New | cookie+CSRF+明示確認、同controller要求、状態へ戻す |
| GET /login | Out of scope/unchanged | HTTPS login/認証情報を出力しない |
| POST /login | Out of scope/unchanged | 既存認証契約を維持 |
| POST /logout | Out of scope/unchanged | 既存cookie認証を維持 |
| GET /health | Out of scope/unchanged | healthを移行成功と誤認しない |

発見済み対象15identities=既存domain9(unchanged6/extended3)+new2+shared out-of-scope4。アプリ全体のroute総数ではない。`/execution`は存在せず導入しない。既存root/races/jobs/settingsのHTML fallbackは変更しない。既存operation responseのfieldを削除/必須化せず、新しいphase/reason値のみを追加する。最小hostで通常stage/executeを解禁しない。

## Experience, mock and documentation

既存migration detailから開始する。既知preApplyingの要求候補だけ「準備を中止」を表示し、確認欄に「業務データを変更しない・元依頼とbackupを保持・収集停止の通常画面へ戻す・安全確認できなければ中止しない」を示す。表示は解除保証ではなく、実行時のcontroller証明が必須。Applying/Partial/不明では操作を出さず理由を表示する。確認→要求の2操作/別page遷移なし、処理中は確認中/最終更新時刻を表示して二重送信を禁止。拒否・通信失敗・再読込でも依頼番号と保存結果を保持する。

状態例: `準備が中断されました → [準備を中止] → 確認 → 元の依頼:失敗(準備中止) / 収集:停止 → [新しい移行依頼を作成]`。後者は入力を引き継ぐだけで、自動stage/Applyはしない。

canonical [管理UI設計](../../20-admin-ui-design.md)へ未承認の追加案としてリンクする。承認時は親の最小host境界・route ledger・mockにも取消条件を反映し、既存の承認境界を黙って書き換えない。

[PC/390px状態モック](mocks/preparation-cancellation.md)を同時更新。通常Blazorは既存FluentButton/確認入力を再利用し、primary actionは一つ。最小hostは既存semantic HTML/CSRF formを維持する。field errorと安全理由を操作近くへ置き、keyboard/focus/長い番号の折返し/再接続を検証する。自動stageではなく「新しい移行依頼を作成」へ入力を引き継ぐだけ。

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | 最小hostに専用取消endpointを追加する。現在journal phaseとoperation終端更新は別method | 誤解除で並行書込み/不明データを起動する危険 | preApplying厳守、全writer/lease/gate/pause証明、atomic要求/終端、CLIと直列化、crash/競合test。証明不能は保護継続。代案は外部手動復旧で画面完結要望に合わない | AC1,AC2/T2,T5 | Leadとreadonly Luna支持、技術的異議なし | 承認待ち | Resolved in design |
| C2 | 26Oct実source: 強制operator-only flagのままでは正常画面へ戻れない | 解除してもUIが戻らない、またはgeneric bypassで別操作を誤解除 | 既存workflow対象GUIDを起動設定にbind、同GUID/sourceの確定取消と全gate解除/Readyのみ解除。別GUID/欠落/未知は保護継続。新token/service不要 | AC1,AC2/T5、別対象receipt反例 | Lead支持、宣言的bindと既存journalを再利用 | 承認待ち | Resolved in design |
| C3 | StoppingApi単独は現在のprocess/gatesや全データ履歴の証明ではない | phaseだけで解除すると不明状態の解禁になる | 現行/旧codeのwrite-aheadを根拠とし、実行時に全writer終了/locks/gates/pauseを再証明。未知は拒否/保護UI維持。元24Oct原因を推測で確定しない | AC2/T2,T5 | Lead支持、断定的な取消可能判定へ反対 | 承認待ち | Resolved in design |

残存risk: Applying記録後の無書込みcrashも保守的に停止する。これを推測で解除しない。runtime proofに反例があれば実装を止め再計画する。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 開始前を実行時に証明した依頼だけ画面から明示中止し、元依頼/backup/業務データを保持して収集停止の通常画面へ戻せる。対象bindされた保護flagだけ解除し、新依頼/移行は自動実行しない | T1,T2,T3,T4,T5 | 実SQLite+真のLinuxPID1、現在protected modeからのAPI/実browser成功・再接続、DB/backup hash不変 | Not started |
| AC2 | Applying以後/Partial/不明/証拠不一致/非pause/別targetは拒否。CLI競合/重複/応答消失/各crashで二重書換え・解除・起動なし。取消失敗も保護画面に安全な理由/時刻を保持 | T1,T2,T3,T4,T5 | pure policy、atomic journal、real process競合、cookie/CSRF、390px/keyboard/拒否UI。必要journal移行は旧row保持/cold start証明 | Not started |

## Task plan and review gates

- T1 — Lead / architecture・public contract/data判断。現行source/旧2872の根拠、concerns、利用者承認と親scope反映。通常実装はしない。26Oct read-only Luna T4-A2の事実をLeadがsource再読し反映。AC1,AC2。State: Proposed。
- T2 — Planned Luna/high journal/policy executor。承認後、Journal/ExecutionPolicy/OperationContractsと対応SQLite/policy/endpoint testsをexclusive ownership。要求とApplyingの競合、atomic取消receipt/旧row保存、純粋policyを最小test→関連Release/build/formatで検証。I/OやUIを所有しない。AC1,AC2。State: Dependent。
- T5 — Planned Luna/high controller/gate executor。T2 contract freeze後、Supervisor/RuntimeMode/MaintenanceOwnership/Endpointsと対応nativePID1/API tests、既存workflow/helperの対象GUID bindをexclusive ownership。T2と共有fileはowner handoff後だけ変更。全writer終了/locks/gates/正常復帰/別target拒否と全crash点を実証。AC1,AC2。State: Dependent。
- T3 — Planned Luna/high UI executor。T2/T5 contract freeze後、AdminApiClient.HorseIdentityMigration.cs、HorseIdentityMigrationAdmin.razor、AdminComponentTests/AdminBrowserTestsをexclusive ownershipで変更。最小host HTMLの変更はT5に返し共有fileを同時編集しない。AC1,AC2。bUnitと実browserで通常/最小画面のkeyboard/390px/再接続/失敗保持/二重送信を検証。State: Dependent。
- T4 — Lead integration/deployment/acceptance（integrationと本番権限判断、機械的検証はLuna/high）。T2/T5/T3後、exact CI/formatter/native/browser/backup/source hashを統合、一致するworkflowのterminal/production確認、明示取消と通常画面/収集停止確認。新移行と再開は別操作で親の承認済み範囲へ接続する。AC1,AC2。State: Dependent。

2026-10-19 Design/task-split review: Leadとreadonly Luna plannerが既存起動順序、journal、phase、gateの不足を確認。cancel-onlyは同GUID再Applyやpartial backup再作成を増やさず、安全条件を限定できる。workerはコード/環境を変更していない。機械的実証は実装後に必須。Pre-implementation review/具体file/test auditは承認と親coreの実装計画が揃ってから行い、通常の内部配置判断で再質問しない。

26Oct Design/task-split/concern review: Lead accepts read-only T4-A2の契約事実を現行/旧sourceで照合し、古いmode/GET前提を修正。新規取消はProposedのまま、UI復旧承認から推定しない。C1–C3は設計で解決、全user dispositionは承認待ち。重要判断は専用endpoint/atomic要求とreceipt/対象bind解除とし、曖昧なgeneric unlockを却下。metadataとcontrollerを直列分割し、通常実装をLuna/highへ戻す。画面は既存詳細から確認2操作、追加dashboard不要とself-reviewした。現在の課題がこの条件を満たすかは実行時検証であり、未観測状態を成功前提にしない。

## Documentation updates

- `docs/20-admin-ui-design.md`: 未承認の取消UI案と本record/mockへの参照。既存保護画面の実装済み仕様と区別する。
- `docs/11-automation-design.md`: 未承認の対象GUID-bound operator解除案。現在の強制保護を自動解除可能とは記載しない。
- 親24Oct復旧recordはUI復旧完了と未承認取消/移行未完了を分離する。承認後に本record/親の境界を同時更新する。
