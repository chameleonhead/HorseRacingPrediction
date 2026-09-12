# 収集障害の対応状態ライフサイクル

- Status: Implemented
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-12
- Updated: 2026-09-13

## Context

現在の`CollectionFailureNotification`は`PublishedAt`だけを持ち、SNS等への通知配信状態と、運用者が対応すべき障害の解決状態を分離していない。管理API/UIの「要対応」「障害のまとまり」も`PublishedAt == null`を参照する。

そのため、手動再取得や通常Recoveryで新Taskが開始しても過去の失敗が障害のまとまりへ残り得る。一方、SNS通知が成功しただけで、未解決障害がUIから消える可能性もある。

## Goals

- 通知を外部へ送ったかと、障害が未対応かを独立して管理する。
- Recoveryまたは手動再取得が開始した障害を「要対応」から外し、対応中として追跡する。
- Recovery成功時に解決済みとし、再失敗時は最新の失敗だけを要対応として表示する。
- Resourceの現在状態と障害表示を一致させ、過去の失敗が最後の状態として残らないようにする。

## Non-goals

- 失敗履歴自体を削除しない。
- 失敗したTaskやAttemptを書き換えない。
- SNS通知済みという理由だけで障害を解決済みにしない。

## Experience and interaction design

- 「要対応」「障害のまとまり」には現在Openな障害だけを表示する。
- 再取得を受け付けた時点で該当障害を「対応中」に移し、要対応件数から除外する。
- 対応中Taskが成功すれば「解決済み」、失敗すれば新しい原因・時刻の障害をOpenにする。
- Resource詳細では過去の障害、対応開始Task、解決結果を時系列で確認できる。
- 必要に応じて運用画面で「対応中」「解決済み」を履歴フィルターとして確認できるが、通常の障害のまとまりには含めない。

## Documentation updates

- `docs/changes/20260911_unified-collection-platform/README.md`: Failure notificationとoperational resolutionの責務分離、検証結果を追記する。
- `docs/changes/20260911_unified-collection-platform/admin-ui-implementation-note.md`: 要対応・対応中・解決済みの表示規則を追記する。

## Technical impact

- Failure notificationへ`ResolutionStatus`（Open / RecoveryInProgress / Resolved / Superseded）、`RecoveryTaskId`、`RecoveryStartedAt`、`ResolvedAt`を追加する。
- `PublishedAt`は外部通知の配送証跡だけに使用する。
- 通知service用の`GetUnpublishedFailureNotificationsAsync`と、管理UI用の`GetActionableFailureNotificationsAsync`を分離する。
- Recovery APIとResource詳細の手動再取得は、同じResource + DefinitionのOpen障害を新しいTaskへ関連付け、transaction内でRecoveryInProgressへ更新する。
- Task成功時は関連するOpen/RecoveryInProgress障害をResolvedへ更新する。
- Recovery Task失敗時は古い障害をSupersededとし、最新Attemptの新しい障害だけをOpenにする。
- Recoveryの取消・lease回収・dispatch失敗では、実行可能な後続Taskがなければ障害をOpenへ戻す。
- SQS messageのack、dispatch generation、Task statusはFailure resolutionとは独立させる。SQS再配信だけで障害を解決・再開しない。

## Decisions

- 「対応済み」は履歴削除ではなくresolution stateとして表現する。
- Recovery受付時点で要対応から除外する。Active Taskが存在するため、同じ問題を再操作させない。
- 成功した任意の後続Taskが同じResource + DefinitionをCurrentへ更新した場合、古いOpen障害も解決済みにする。Recovery reasonだけに依存しない。
- 新しい失敗が発生した場合は最新FailureをOpenにし、古いFailureはSupersededとして履歴に残す。

## Acceptance criteria

1. SNS通知がPublishedになっても未解決Failureは「要対応」に残る。
2. Recoveryまたは手動再取得を受け付けると、FailureはRecoveryInProgressとなり「障害のまとまり」から消える。
3. 関連TaskがPending/Runningの間、同じFailureへ重複Recoveryを作成できない。
4. 関連Task成功時、FailureはResolvedとなり、ResourceのCurrent状態と一致する。
5. 関連Task失敗時、古いFailureはSuperseded、最新FailureだけがOpenになる。
6. Recovery Task取消または後続なしのdispatch失敗時、未解決FailureがOpenへ戻る。
7. 過去のTask/Attempt/Failure履歴は削除されず、Resource詳細で追跡できる。
8. UIの要対応件数、障害のまとまり件数、一覧内容が同じActionable queryを使用する。
9. 複数API instanceから同時Recoveryされても、1つのActive Taskと1つのRecoveryInProgress遷移だけが成立する。
10. SQS重複配信、古いdispatch generation、lease expiryだけではResolvedへ誤遷移しない。

## Delivery plan

1. Published/Actionable混同を再現するStore/API/UIテストを追加する。
2. Failure resolution schemaとmigrationを追加する。
3. Request/Complete/Cancel/Watchdogとresolution遷移を同じtransactionへ接続する。
4. 通知serviceと管理queryを分離する。
5. Jobs、Operations、Resource詳細を共通Actionable projectionへ接続する。
6. concurrency、SQS duplicate、Recovery再失敗、restartを検証する。
7. 本番デプロイ後、現在残っているFailureを後続Task状態から安全にmigration/reconcileする。

## Verification record

- 2026-09-12: 現行Store/APIを調査。`GetPendingFailureNotificationsAsync`は`PublishedAt == null`だけを条件にし、SNS通知、Jobs画面、Operations画面、Recovery選択が同じqueryを共有している。Resource詳細の通常再取得はFailure notificationを更新せず、Recovery endpointだけが`MarkFailureNotificationPublishedAsync`を呼んで表示対象から外している。
- 2026-09-12: schema v4として対応状態、関連Recovery Task、対応開始・解決日時を追加した。新規DBと履歴テーブルのない既存DBを列検査で識別し、v3からは非破壊ALTERで移行する。
- 2026-09-12: Request、成功・失敗完了、取消、lease取消、dispatch試行超過を同一transaction内のresolution遷移へ接続した。成功はResourceがCurrentになった場合だけ解決し、再失敗は旧障害をSupersededとして最新障害をOpenにする。
- 2026-09-12: 外部通知用Unpublished queryと管理用Actionable queryを分離し、dashboard、Jobs、障害group、Recovery選択をActionableへ統一した。Resource詳細には全対応状態の障害履歴を追加した。
- 2026-09-12: Store project単体のRelease buildは警告0・エラー0。Store/API/UIのライフサイクルテストを追加した。共有作業中のmicrobatch契約変更によりsolution buildは`CollectionPlatformStore.cs`の`PendingCollectionDispatch`引数不足で停止しており、統合後の全テスト実行とStatus=Implemented更新を残す。
- 2026-09-13: microbatch契約との統合後にRelease solution build（警告0・エラー0）、Collector 142件、API 170件（既存skip 1件）を含む関連テストを完走した。通知配信済みでもOpen障害は要対応に残り、Recovery開始後は対応中、Current到達時は解決済み、再失敗時は最新障害だけがOpenになることを確認し、StatusをImplementedへ更新した。
- 2026-09-13: 本番確認で、障害groupはRecovery開始後に消える一方、Jobsの「要対応」がTaskのFailed/DeadLetter状態だけを参照して旧失敗を表示し続ける不整合を検出した。Task検索へ`ActionableOnly`を追加し、要対応一覧と件数をOpenなFailure notificationへ統一した。旧Failed Taskは履歴検索には残る。Store回帰テスト、Collector 142件、API 170件（既存skip 1件）、Release solution build、format検証を完走した。
