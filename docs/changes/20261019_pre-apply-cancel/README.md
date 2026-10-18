# 書き換え開始前の移行準備を画面から中止する

- Status: Proposed
- Change record schema: 2
- Owner: Lead
- Created: 2026-10-19
- Updated: 2026-10-19
- JRA site contract impact: None

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | 利用者承認待ち。親の独立した承認済みデータ検証は継続する |
| Verification | Not started | 下記の取消・競合・再起動・拒否・UI検証 |
| Deployment/operation | Not started | 親の配備・本番復旧に統合。完了を親から除外しない |

## Context and goals

[親の管理画面内復旧](../20261017_admin-screen-recovery/README.md)の実行制御を調査した結果、maintenance gate作成後・Applying記録前の異常終了では、業務データを書き換えていなくても最小画面に留まる。親の承認済み最小画面はSucceededの明示readonly再検証しか変更を許さず、この準備段階から画面だけで戻る経路がない。

書き換え未開始を厳密に証明できた場合に限り、利用者の明示操作で準備を中止し、収集停止のまま通常画面へ戻す。元の依頼は失敗記録として残し、既存バックアップは保持する。再試行は新しいstage/GUIDで行う。同じGUIDのApply再開、バックアップ上書き、未知状態の解除は追加しない。

## Decisions and safety boundary

- Applyingは固定CLI起動前に永続化するwrite-ahead境界。Applying以後は、実際にCLIが起動していなくても取消不可。
- 親PID1による全owned API/CLI終了確認、正確なsource-set/gate一致、全sourceのexclusive control/runtime leases、persisted pauseの再確認を必須にする。不明・破損・不足はgateを維持する。
- control lease下でCLI開始と取消を直列化し、intent terminal failureとoperation Failedを1つのjournal transactionで記録する。その後に一致するgateだけを解除する。途中再起動時は同じ終端結果から解除完了だけを安全に再確認し、Applyを起動しない。
- 重複・応答消失後の取消は同じreceiptを返す。二重解除・二重API起動はしない。正常APIの起動はSupervisorだけが行い、収集は停止を維持する。
- 業務DBの書換え、データ削除、バックアップの削除・上書き、自動取消、自動Apply、部分移行の巻戻しは対象外。

## Finite public-contract ledger

| Method/path | Disposition | Invariant |
| --- | --- | --- |
| POST /api/v2/admin/horse-id-migration-operations/{operationId}/execute | 親の新設予定routeへclosed CancelPreparation modeを追加 | 認証後、同じ固定GUIDだけ。Apply/VerifyOnlyの安全条件と既存receiptを維持 |
| GET /api/v2/admin/horse-id-migration-operations/{operationId}/execution | 親の新設予定statusへ取消可否・安全な理由を追加 | journal/control証拠由来。nonce/DBパス/例外全文を返さない |

影響する2routes=extended2、新しいroute0。親の全12routesを増減せず、他10routesは不変。

## Experience, mock and documentation

既存migration pageを使用する。PC/390px共通で、証明できた準備中断時だけ「準備を中止」を表示し、確認欄に「データは変更しない・バックアップを保持・収集停止のまま戻る」を示す。Applying/不明時は中止ボタンを出さず停止理由を表示。処理中は二重送信を禁止し、失敗時は依頼と理由を保持する。

状態例: `準備が中断されました → [準備を中止] → 確認 → 元の依頼:失敗(準備中止) / 収集:停止 → [新しい移行依頼を作成]`。後者は入力を引き継ぐだけで、自動stage/Applyはしない。

canonical [管理UI設計](../../20-admin-ui-design.md)へ未承認の追加案としてリンクする。承認時は親の最小host境界・route ledger・mockにも取消条件を反映し、既存の承認境界を黙って書き換えない。

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | 最小hostに新しい取消modeを追加する。現在journalのphase更新とoperation終端更新は別method | 誤った解除で並行書込みや不明データを起動する危険 | preApplying厳守、全process/lease/gate/pause証明、atomic終端記録、CLIと直列化、再起動/競合テスト。証明不能は停止。代案は外部手動復旧で、画面完結要望に合わない | AC1,AC2/T2 | Leadおよびreadonly Luna planner支持、技術上の異議なし | 承認待ち | Resolved in design |

残存risk: Applying記録後の無書込みcrashも保守的に停止する。これを推測で解除しない。runtime proofに反例があれば実装を止め再計画する。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 書換え未開始を証明した依頼だけを画面から明示取消し、元依頼とbackupを保持し、収集停止の通常画面へ戻せる | T2,T3,T4 | 実SQLite+真のLinuxPID1プロセス、API/実browserの成功・再接続 | Not started |
| AC2 | Applying以後/Partial/不明/証拠不一致/非pauseは拒否。CLI競合/重複/応答消失/各途中crashでも二重書換え・解除・起動なし | T2,T3,T4 | phase境界/atomic journal/real process競合と失敗UI。DB/backup不変 | Not started |

## Task plan and review gates

- T1 — Lead / High / persistence・public contract判断。read-only設計と利用者承認、親のscope反映。State: Proposed。AC1,AC2に関連し、承認証拠を確認する。
- T2 — Planned Luna/high lifecycle executor（具体model/監査はdispatch時）。親T3 coreのsource freeze後、Journal/ExecutionPolicy/SourceGate/Supervisor/Endpoints/OperationContractsと対応journal・gate・endpoint・LinuxPID1テストをexclusive ownershipで変更。AC1,AC2。最小focused→関連Release tests/build/exactformat/graph/diff、truePID1の実証。State: Dependent。
- T3 — Planned Luna/high UI executor。T2 contract freeze後、AdminApiClient.HorseIdentityMigration.cs、HorseIdentityMigrationAdmin.razor、AdminComponentTests/AdminBrowserTestsをexclusive ownershipで変更。AC1,AC2。bUnitと実browserのkeyboard/390px/再接続/失敗保持/二重送信を検証。State: Dependent。
- T4 — Lead integration/deployment/acceptance（integrationと本番権限判断）。親T8/T6のCI配備と状態確認へ統合し、両記録を同期する。AC1,AC2。State: Dependent。

2026-10-19 Design/task-split review: Leadとreadonly Luna plannerが既存起動順序、journal、phase、gateの不足を確認。cancel-onlyは同GUID再Applyやpartial backup再作成を増やさず、安全条件を限定できる。workerはコード/環境を変更していない。機械的実証は実装後に必須。Pre-implementation review/具体file/test auditは承認と親coreの実装計画が揃ってから行い、通常の内部配置判断で再質問しない。
