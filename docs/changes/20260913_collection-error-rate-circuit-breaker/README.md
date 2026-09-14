# 想定外収集エラーによる全体停止とSNS通知

- Status: Approved
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-13
- Updated: 2026-09-13

## Context

新収集基盤への切替時に、旧 `JobFailureNotificationDispatcher`、`SnsJobFailureNotificationPublisher` と、旧DLQ reconcilerが持っていた全体停止・SNS送信処理が削除された。新基盤には失敗通知レコード、手動の全体停止、SNS publisher、DLQ reconcilerの型と設定はあるが、それらを接続する実行経路がない。

そのため現在は、個別Taskが大量に `Failed` / `DeadLetter` になってもSNS通知は送信されない。新 `CollectionPlatformDeadLetterReconciler` もDLQをTask状態へ反映するだけで、設定済みの `ConsecutiveFailureThreshold` を参照せず、全体停止やSNS publisherを呼ばない。`ICollectionPipelineAlertPublisher.PublishCollectionStoppedAsync` のproduction callerは0件である。

TerraformのLambda error、throttle、SQS DLQ、oldest-message CloudWatch alarmは同じSNS topicを参照するため、アプリ通知とは独立している。ただし電話番号secretが空ならSNS subscription自体は作成されない。

## Goals

- 想定外の収集エラーが1件発生した場合、新しいTaskの配送・取得を直ちに自動停止する。
- 自動停止の理由、対象Task・Resource、Attempt結果、エラー分類と発生時刻をSNSへ送信する。
- SNS送信に失敗しても停止状態を失わず、送信成功まで再試行する。
- 正常な業務結果である「未公開」「対象なし」「取消」で全体停止しない。
- 管理画面から原因を確認し、手動再開できる。

## Non-goals

- 実行中Workerを強制終了すること。
- 自動復旧・自動再開すること。
- SNSの電話番号やsubscription confirmationをアプリから管理すること。
- すべての個別失敗を1件ずつSNS送信すること。大量障害は一つの停止インシデントへ集約する。

## Experience and interaction design

通常時はTask完了経路が想定外エラーを確定するのと同じ永続化処理で、pipelineを停止する。別周期の率計算や連続判定を待たない。Attemptを開始できずwatchdogまたはDLQ reconcilerがTaskをterminal化する場合も、その確定処理で停止する。

停止後は既存のジョブ画面に停止理由を表示する。停止理由には対象Task・Resource、Attempt結果、error code・概要と発生時刻を含める。停止中は新規SQS配送と新規lease取得を拒否するが、実行中Workerの完了報告、管理画面、失敗確認、再取得要求の登録は利用可能とする。再取得要求は停止解除まで待機する。

SNSは停止への状態遷移ごとに一つ送る。送信失敗時は未送信状態を永続化し、次回監視周期で再試行する。このため障害時配送はat-least-onceであり、SNS側の応答喪失時には重複し得る。メッセージにはインシデントIDを含め、重複を判別可能にする。

再開は既存の手動操作だけで行う。再開時に連続超過回数をリセットし、再開以前のAttemptを次の判定窓へ持ち越さない。

## Documentation updates

- `docs/22-collector-design.md`: 新収集基盤の想定外エラー一発停止、停止範囲、SNS通知の正本へのリンクを追記する。
- 本変更記録: 閾値、判定対象、通知再試行、受け入れ基準の正本とする。

## Technical impact

- `CollectionPlatformStore`へ、想定外エラーのterminal確定と停止インシデント作成を同じtransactionで行う処理を追加する。
- 既存 `ICollectionPipelineAlertPublisher` / `SnsCollectionPipelineAlertPublisher` を停止インシデント通知serviceへ接続する。
- 通知状態は失敗通知の解決状態とは別に永続化する。
- 想定内・想定外の分類は既存のAttempt resultとerror codeを用いる。率・母数・連続回数の設定は追加しない。
- `JobFailureNotifications:TopicArn` は引き続きSNS topic ARNの入力とする。`Enabled` は停止アラートを無効化しない。
- deploy workflowでtopic ARNが空なら失敗する現行gateを維持し、TerraformでSMS subscription数とconfirmation状態を検証可能にする。

### 停止対象

`TransientFailure`, `PermanentFailure`, `ParseFailure`, `ValidationFailure`, `UnexpectedPage`, `AccessLimited` と、watchdog/DLQによるterminal化を想定外エラーとして一発停止の対象にする。既存retry policyが一時エラーを再試行する場合は、再試行可能な中間状態では止めず、Taskがterminal failureとして確定した時点で止める。

`ResourceNotYetAvailable`, `ResourceNotFound`, `Cancelled` は想定内の業務結果として停止対象から除外する。`Succeeded`、実行中、待機中、再試行待ちも停止対象ではない。

## Decisions

- 率・母数・連続判定は行わず、想定外エラーでTaskがterminal failureになった最初の1件で停止する。
- pipeline停止は既存の永続 `collection_platform_controls` とlease/acquisition/outbox guardを使用する。
- 先に停止状態をatomicに確定し、その後SNSを送る。SNS障害によって収集が継続する危険を避ける。
- 自動再開はしない。原因未解決の再開ループを避ける。
- CloudWatch infrastructure alarmは残す。アプリの収集成功率とAWS transport異常は別の観測対象である。

## Acceptance criteria

| ID | Observable criterion | State |
|---|---|---|
| AC1 | 想定外エラーで最初のTaskがterminal failureになるのと同じ永続化境界でpipelineが一度だけ停止する。 | Verified |
| AC2 | 停止後はoutboxの新規配送とWorkerの新規lease取得が止まり、実行中Attemptの完了報告と管理画面は利用できる。 | Verified |
| AC3 | 正常な未公開、対象なし、取消、成功、再試行可能な中間状態では停止しない。 | Verified |
| AC4 | 停止SNSにインシデントID、対象Task・Resource、error code・概要、発生時刻、管理画面URLが含まれる。 | Verified |
| AC5 | SNS送信失敗時も停止は維持され、再起動後を含めて送信成功まで再試行される。送信済みインシデントは通常周期で再送しない。 | Verified |
| AC6 | 手動再開後、再開前に確定済みの失敗だけを理由に即時再停止せず、新たな想定外terminal failureで再停止する。 | Verified |
| AC7 | DLQ reconciliationとwatchdog terminal化も一発停止の対象となり、Attemptなしの失敗でも停止する。 | Verified |
| AC8 | TopicArn未設定、IAM拒否、SNS一時障害がログと管理状態で識別でき、配備時の空ARN gateが維持される。 | Verified |
| AC9 | 実SNS topicへのpublish smoke testと、SMS subscriptionが存在し確認済みであることを配備後に確認する。 | Not started |

## Delivery plan

1. 想定外エラー分類、停止インシデントと通知状態をモデル化する。
2. Storeでterminal failure確定、atomic pause、再開後の新規障害判定を実装する。
3. hosted serviceから未送信インシデントのSNS送信・再送を接続する。
4. ジョブ画面の既存停止表示へ詳細理由と通知状態を反映する。
5. unit/integration testで最初の想定外エラー、想定内結果の除外、再起動、並行完了、SNS失敗、DLQ/watchdogを検証する。
6. Terraform/deploy contractを検証し、配備後にtopic publishとsubscriptionをsmoke testする。

## Verification record

- 設計前調査: production code上の `PublishCollectionStoppedAsync` callerが0件であることを確認。
- 設計前調査: commit `42a18ad` で旧個別通知dispatcher/publisherと旧DLQ停止処理・テストが削除され、新基盤側に同等の接続がないことを確認。
- 設計前調査: 新DLQ reconcilerが `ConsecutiveFailureThreshold`、pipeline store、SNS publisherを使用していないことを確認。
- terminal failure作成と同じStore transactionでpipeline controlを停止し、原因Failure IDを停止理由へ保存するよう接続した。
- hosted alert dispatcherをSNS publisherへ接続し、成功後だけPublishedAtを更新することで失敗時再送と重複抑止を実装した。
- 再レビュでSNS送信失敗時に`PublishAttemptCount` / `LastPublishError`が更新されない漏れと、停止インシデント検索が未送信先頭1万件に依存する境界を検出した。送信失敗状態の永続化とID直接検索に修正した。
- SNS失敗後にStoreとdispatcherを再生成し、未送信インシデントが再送され、成功後は再送されないテストが成功した。
- 再レビュ後に`dotnet format HorseRacingPrediction.sln --no-restore`、Release build、非ExternalのScraping 214件、Collector 169件、API 185件（1件skip）を再実行し、すべて成功した。
- `ResourceNotFound`では停止しないテスト、DLQ一発停止テスト、停止通知を一度だけpublishするテストが成功した。
- `dotnet format HorseRacingPrediction.sln --no-restore`を実行した。Release buildは警告0・エラー0。Collector 160件成功、Api 184件成功・外部依存1件skip。
- 2026-09-15: 現行HEADのStore再開・新規terminal failure、SNS失敗状態永続化・再起動後再送、deploy workflowの空TopicArn gateを再監査し、Release非External solution testsの成功によりAC6/AC8をVerifiedへ更新した。

## Deviations and follow-up

- 実環境でSNS topicのpublish履歴そのものは取得できないため、配備後smoke testを受け入れ基準に含める。
- 実装は完了したが、実SNS publish smoke test未実施のためStatusはApprovedのままとする。
