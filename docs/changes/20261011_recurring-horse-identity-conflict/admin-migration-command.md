# 管理画面からの移行専用コマンド

AC5、2026-10-11。設計案のみ。親recordはProposed。全データ移行の方法・対象・切替契約は [whole-data-migration.md](whole-data-migration.md) と合わせて確定する。

## 利用者の要求

移行専用コマンドを追加し、管理画面から呼び出せるようにする。手動SQLや外部CLI操作だけを完成形にしない。画面に移行ロジックを埋め込まず、一つの移行エンジンをcommandから呼ぶ。画面・API・必要な運用runnerは同じversionの変換ルールを使う。

## Command境界

- サーバー登録済みmigration IDのみを指定できる。任意SQL/shell/command文字列、DBパス、接続文字列、backupパスを利用者から受け取らない。
- 診断、プレビュー、固定計画の実行、状態取得を別operationとして扱う。プレビューではdomain/collectionデータを変更しない。実行履歴・診断レポートの保存は明示したcontrol journalに限定する。
- 画面は認証済みAPIへ要求し、受付後はoperationIdで結果を取得する。長い変換処理をBlazor event callbackや一つのHTTP request内で待たない。HTTP202/受付と移行成功は別。
- 同一plan/fingerprint/idempotency keyの二重送信は同じoperationを返す。環境ごとの同時移行は一つ。再実行時も固定manifestと現データ前提を再検証する。
- planのhash、schema/data generation、対象、backup/restore検証、writer/drain/fenceが一致しない場合は実行を拒否する。UIの確認checkboxを整合性証明としない。
- 実行時の人の確認は対象環境・変更件数・停止影響・復旧境界に紐づける。診断ボタン、ログイン、以前の一般的承認を破壊的apply承認へ読み替えない。
- コアは分類、mapping/closure、typed transform、invariant判定に分ける純粋logicを中心とし、IO/backup/cutover/status更新はexecutorに分ける。任意の移行frameworkや既存resolverの大規模再編はしない。

## APIとUIの生存期間

現在の管理画面はAPIに同居している。APIを停止する全DB切替をAPI内background taskだけで実行する設計は不可。ブラウザー切断、API再起動、プロセスkill後もoperation結果が残ることが必要。

候補は固定commandを動かす独立maintenance runnerと、移行対象DB群の外にあるdurable control journal。受付、実行、復旧、完了検証を同じoperationIdへ記録する。APIが停止している間は画面の接続断を許容し、起動後にそのoperationを再取得する。Docker socket/rootやGitHub tokenをAPIへ勝手に渡す方式は採用しない。

runnerの起動権限・経路・control journal・API再起動方法は現行deploy/maintenance機構を調べて確定する。独立runnerがあるという事実はまだ確認していない。実行口を置くだけでcutover対応済みとはしない。この未確定部分は実装の前提gateであり、UIを先に「実行可」にしない。

M3で確認した結果：現行composeはAPI/Caddyのみ、runtime imageはAPI entrypointのみ。`CollectionRuntimeStatusRecorder`/`CollectionMaintenanceState`はprocess memory、MaintenanceModeはstartup scheduler抑止であって全writer fenceではない。既存CollectionCutover CLIはlegacy jobファイルの限定処理で、runtime imageにも同梱されていない。これらを全データ移行runnerと呼ばない。

実行境界の第一候補は **固定の移行要求を受け取るhost側の単発maintenance runner**。画面/APIは移行ID・計画・operationのtyped要求をdurable inboxへ保存し、runnerは一つの承認済みcommandだけを実行する。journalは移行DB群の外へ置く。必要な起動/停止手順、排他、inbox安全性、最小権限と外部配送gateをT3で決める。イベント駆動の単発起動を優先し、短周期でDBを走査する新しい常駐loopは追加しない。

退ける案：APIにDocker socketや任意shell実行権限を与える、Blazor callbackがAPIをstopして続きを自力実行する、API process-memoryへ進捗だけ記録する。代替はUIで固定requestを作り、運用runnerを手動起動する方式だが、追加の運用操作が必要になる。この違いは未合意のまま隠さない。host runnerはまだ未実装であり、外部配送/権限/切替契約はOpen decision。

## Security / read/report limits

既存のAuthorize管理画面→server-side AdminApiClient→X-Api-Key JSON API境界を守る。APIキーをブラウザー、URL、command arguments、ログへ出さない。cookieで直接実行できるrouteを追加する場合はCSRF/認可契約を別途レビューする。現在の権限モデルに存在しないadmin roleを推測で導入しない。

診断は保存先をserver configで限定し、件数/分類/必要な根拠を返す。バックアップファイル自体、秘密情報や生の全DBdumpをダウンロードする口は作らない。候補と実行履歴はserver-side pagination、報告のサイズ/時間/負荷を制限する。命令や内部exceptionをそのままUIへ表示しない。

## UI

[画面モック](mocks/data-migrations.md)。Settingsから専用Pageへ入り、移行種類を選んで「診断・プレビューを作成」。分類の概要と根拠を確認し、全gateが満たされたplanだけ最終確認して実行する。operation詳細は再訪可能なURLとし、段階、開始時刻、最終更新、処理件数、結果、復旧が必要な理由を表示する。

既存Fluent標準を使い、preview候補はFluentDataGridとpaginationで表示する。狭幅で対象/判定/次の行動を残し、keyboard/focus/labelを確認する。実行中は二重送信を防止するが、閲覧・状態取得は継続する。

## Test boundaries

- 単体：mapping/closure/変換の正常・衝突・未知形状・値保持。
- API/command：認証、allowlist、invalid operation、任意path拒否、preview無変更、stale preview、二重送信、同時操作、transport再送。
- executor：journal永続化、browser/API終了、変換/検証/切替途中kill、異なるgeneration、late delivery、再起動後の状態復旧と完全なtargetチェック。
- bUnit：loading/empty/blocked/error/success、validation、権限、二重送信、operationId保持、再取得。
- browser：Settings→preview→確認→operation詳細、再訪/再接続、keyboard、desktop/narrow、大量・長い値。実行確認はローカル使い捨てデータで行い、本番applyをUIテストとして行わない。

現段階は仕様検討で、command/endpoint/UI/executorは未実装。本番復旧の受け入れも未完了。

## Repository evidence / planned files

- `Api/Web/Components/Pages/Settings.razor:1–3,110–175,546–578`：Authorize、AdminApiClient、owner preview/confirm/apply/progress pattern。移行専用PageをこのSettingsからリンクする案。
- `Api/Web/ApiBrowsing/AdminApiClient.cs:38–51`：API keyはserver-side HTTP headerで付与。Lead CodeGraphで独立確認。
- `Api/CollectionController/CollectionRuntimeStatusRecorder.cs:10–27,104–110`：instanceとaction状態はin-memory。
- `Api/Program.cs:158–182,282–296`：startup MaintenanceModeの境界。`deploy/docker-compose.yml:1–33`と`Dockerfile`：独立runnerなし。
- `tools/HorseRacingPrediction.CollectionCutover/Program.cs`：限定legacy cutover。新migration engineへ流用してdelete scopeを広げない。
- API-hosted UIはFluent 4.14.4（`Api/HorseRacingPrediction.Api.csproj:17–18`）。別Web projectのpreview versionをUI実装の根拠にしない。

実装予定スライス（まだexact scope凍結前）：typed DTOとAPI/client契約、移行専用command/core、isolated control journalとhost runner、専用Blazor page+Settingsリンク、command/API/runner/component/browser tests。既存repair endpointを全DB書換えへ転用しない。shared contractとrunner/cutover契約をLeadが固定後、Luna executorへserialized ownershipで渡す。
