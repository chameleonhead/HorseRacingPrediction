# 収集タスクの取消・キュー／収集データベース初期化・再投入

- Status: Implemented
- Owner: HorseRacingPrediction maintainers
- Created: 2026-08-30
- Updated: 2026-08-30

## Context

収集タスクの状態は Api 管理 SQLite の `jobs` を正本とし、SQS はタスク識別子の配送通知として使っている。一方、現在の通知は `taskId`、`jobType`、`deduplicationKey` だけで投入世代を持たない。このため、SQS に残った古い通知が、管理画面から再投入されて再び `Ready` になった同一タスクを取得する可能性がある。

また、既存の管理機能には個別の「再投入」はあるが、「取消」、待機・実行中タスクの一括取消、SQS と DLQ の消去、実行中リースの失効を一つの安全な復旧操作として行う手段がない。SQS の `PurgeQueue` は不可逆で、完了まで最大 60 秒かかり、その間に送信されたメッセージも削除され得るため、outbox dispatcher の一時停止と組み合わせる必要がある。

ここでいう「バージョン遅れのタスク」は、現在のタスク投入世代より古い SQS 通知および、取消・再投入より前に取得された実行リースを指す。

参考:

- [AWS SQS PurgeQueue API](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/APIReference/API_PurgeQueue.html)
- [AWS Lambda と SQS の少なくとも 1 回配送](https://docs.aws.amazon.com/lambda/latest/dg/with-sqs.html)

## Goals

- 個別タスクを安全に取り消し、古い SQS 通知や実行中 Worker の結果を無効化できる
- 取り消したタスクを明示的に選び、最新世代として再投入できる
- 全タスクの実行状態、未配送 outbox、SQS 本体、DLQ を運用上まっさらな状態へ戻せる
- 収集済みデータと収集タスク状態をバックアップ後に完全消去し、空のスキーマから収集をやり直せる
- 取消・初期化・再投入を監査でき、誤操作と競合を検出できる
- 初期化中に新しい通知が消失しない
- ジョブ詳細の戻るリンク、親子ジョブリンク、セクションリンクが必ず実在する遷移先を指す

## Non-goals

- 通常のキュー初期化における成功済みタスク、失敗履歴、操作監査の物理削除
- 実行中 Lambda プロセス自体の強制終了
- Collector Lambda のデプロイバージョン切替やロールバック

## Experience and interaction design

### 個別タスク

タスク詳細に次の操作を表示する。

- `取り消す`: `Ready`、`Running`、`Pending`、`WaitingDependency`、`Failed`、`DeadLetter` を `Cancelled` にする。理由を必須入力とし、表示時点から更新されていれば 409 で中止する
- `再投入する`: 既存どおり確認を挟み、理由を入力して新しい投入世代を発行する。`Cancelled` を含む任意の既存状態から実行できる

取消後もタスク詳細と履歴は残す。取消操作は SQS の特定メッセージを探索・削除せず、投入世代を進めることで既存通知を無効化する。これにより SQS 標準キューで個別メッセージを確実に探す必要をなくす。

### キューをまっさらに戻す

収集タスク一覧に `キューを初期化` を管理者向け危険操作として置く。実行前に次をプレビューする。

- 取消対象の状態別タスク数
- 無効化する未配送 outbox 数
- SQS 本体と DLQ が消去対象であること
- 収集済みデータと履歴は削除されないこと

理由入力と確認文 `キューを初期化` の入力を必須にする。実行後は進行状態を `取消処理中`、`SQS 消去中`、`安定化待ち`、`完了` または `失敗` として表示する。処理中の再実行は拒否し、途中失敗時は完了済み工程と再試行可能な工程を表示する。

「まっさら」は、操作完了時に実行可能・実行中・依存待ちの収集タスクが 0 件、送信可能な outbox が 0 件、SQS 本体と DLQ の既存通知が消去済みで、古い世代の通知やリースが取得・完了できない論理状態を意味する。AWS の近似メトリクスが即時に 0 になることまでは完了条件にしない。

### 収集データベースを完全初期化する

キュー初期化とは別に、より強い危険操作 `収集データを完全初期化` を用意する。この操作は次を空にする。

- `/data/eventstore.db`: EventFlow のイベント・スナップショットと、レース、出走、馬、騎手、調教師、馬主、予想、メモを含む全 ReadModel
- `/data/collection-tasks.db`: ジョブ、marker、outbox、取得状態、日次状態、失敗通知、操作監査
- SQS 本体と DLQ

つまり、収集由来か手入力かを問わず Event Store 内のデータはすべて失われる。スキーマと EF migration history は最新状態で再作成し、初回収集を投入できる空の環境に戻す。

実行前画面には両 DB のファイル名、テーブル別件数、SQS/DLQ、消去される予想・メモ・手動補正を明示する。理由、確認文 `収集データを完全初期化`、再認証を必須とする。キューだけの初期化と視覚的に分離し、通常操作から直接実行しない。

完全初期化の直前に SQLite Online Backup API で両 DB の整合したバックアップを `/data/backups/full-reset-<UTC timestamp>/` に作成し、`PRAGMA quick_check` 成功を確認する。バックアップ作成または整合性確認に失敗した場合は消去しない。完了画面にはバックアップパスと、復元にはサービス停止が必要であることを表示する。バックアップの自動削除は行わず、既存の世代管理とは別扱いにする。

完全初期化の進行記録は消去対象 DB 内へ置かず、`/data/reset-state.json` に一時保存する。これによりプロセス再起動後も「バックアップ済み」「キュー消去済み」「DB 再作成済み」を判定して再開できる。完了後の記録はアプリログとバックアップディレクトリ内の manifest に残し、通常画面には直近結果だけを表示する。

### ジョブ詳細リンクの修正

調査により次の不具合を確認した。

- Api の `CollectionTaskDetail.razor` は `/collection-tasks/{id}` 上で `collection-tasks` と `collection-tasks/{id}` を相対指定しているため、戻るリンクや親子リンクが `/collection-tasks/collection-tasks...` に解決される
- Collector の `JobDetail.razor` も `/jobs/{id}` 上で `jobs` と `jobs/{id}` を相対指定しており、同様に `/jobs/jobs...` へ遷移する
- Api の詳細タブは、エラーまたは親子タスクがない場合も `#errors`、`#relations` を表示する一方、対象 section は描画されない

アプリ内ルートは先頭 `/` 付きの絶対パスへ統一する。詳細タブは対象 section と同じ条件で表示し、描画されないアンカーを出さない。Api と Collector の重複画面を両方修正し、深い URL から戻るリンクと親子リンクを操作するルーティングテストを追加する。

## Technical impact

### 投入世代によるフェンシング

- `jobs` に単調増加する `dispatch_generation` を追加する
- `collection_dispatch_outbox` と `CollectionTaskNotification` に同じ世代を保存する
- Worker の acquire は `taskId + dispatchGeneration` が現在世代と一致するときだけリースを発行する
- 個別取消、一括初期化、手動再投入は世代を進める
- 取消・再投入前の lease token は消去し、旧 Worker の complete/fail/requeue を既存の lease token 照合で拒否する
- 世代不一致の SQS 通知は成功扱いで破棄し、DLQ へ送らない

既存通知との移行互換のため、世代を持たない通知は `generation = 0` と解釈する。マイグレーション時の既存ジョブも 0 とし、最初の取消または再投入から 1 以上になる。

### 個別取消・再投入

- Store に楽観的同時実行制御付き `CancelJobAsync(jobId, expectedUpdatedAt, actor, reason)` を追加する
- 取消では状態を `Cancelled` にし、リースを失効し、世代を進め、同じタスクの未配送 outbox を無効化する
- 手動再投入では世代を進めたうえで、その世代を持つ outbox を一件作る
- `job_operation_audits` に `ManualCancel`、`ManualRequeue`、操作者、理由、前後状態を保存する
- 親子タスクは自動連鎖取消ししない。親を取り消す際に未完了の子があれば確認画面へ表示し、利用者が個別取消または全体初期化を選べるようにする

### 全体初期化

初期化状態を Api DB に永続化し、単一の実行だけを許可する。処理順は次のとおりとする。

1. 初期化状態を開始し、outbox dispatcher を停止状態にする
2. 成功済みを除く全収集タスクを `Cancelled` にし、世代とリースを失効する
3. 未配送 outbox を無効化する
4. SQS 本体と DLQ に `PurgeQueue` を要求する
5. 60 秒の安定化期間中は dispatcher を停止したままにする
6. dispatcher を再開し、初期化状態を完了にする

API プロセスが途中で再起動しても、永続化した工程から再開する。論理取消を先に行うため、初期化開始前の通知を Lambda が受信しても acquire できない。初期化中にスケジューラが作った新規タスクは DB/outbox に保持するが dispatcher から送信せず、安定化後に送信する。

SQS クライアントに main queue と DLQ の purge を追加し、Lightsail Api IAM policy に両キューの `sqs:PurgeQueue` を許可する。キュー未設定のローカル環境では DB 側の論理初期化だけを行い、その旨を結果へ記録する。

### 収集データベース完全初期化

完全初期化は次の順に実行する。

1. maintenance mode を開始し、reset 状態照会以外の更新 API、スケジューラ、outbox dispatcher を停止する
2. キュー初期化と同じ世代・リース無効化を行う
3. SQS 本体と DLQ を purge し、60 秒の安定化期間を終える
4. `eventstore.db` と `collection-tasks.db` を Online Backup API で専用ディレクトリへバックアップし、元 DB とバックアップを `quick_check` する
5. SQLite の接続を閉じて connection pool を解放し、DB 本体と対応する `-wal`、`-shm` の正規化済みパスが設定済み `/data` 配下であることを検証する
6. 両 DB ファイルを削除し、Event Store は EF migrations、タスクストアは最新 schema initializer で空の DB を再作成する
7. 空であることと schema/integrity を検証して maintenance mode を解除する

削除対象パスが設定済みデータディレクトリ外、ルート、home、シンボリックリンク経由になる場合は中止する。DB 削除後の再作成に失敗した場合は maintenance mode を維持し、自動でバックアップを上書き復元せず、manifest に基づく明示的な復元手順を案内する。

通常のキュー初期化は履歴を保持するが、完全初期化は `collection-tasks.db` 自体を再作成するためタスク履歴・監査も消える。完全初期化を実行した事実だけは消去対象外の manifest とアプリログに残す。

### API と UI

- `POST /api/collection/tasks/{jobId}/cancel`: `expectedUpdatedAt`、`reason` を受ける
- `POST /api/collection/tasks/{jobId}/requeue`: 既存 request に `reason` を追加する
- `GET /api/collection/reset`: 最新の初期化状態とプレビューを返す
- `POST /api/collection/reset`: 理由と確認文を受け、非同期の初期化を開始する
- `POST /api/collection/reset/full`: 再認証、理由、完全初期化の確認文を受ける
- タスク詳細に取消と再投入、一覧にキュー初期化パネルと分離した完全初期化パネルを追加する
- ジョブ詳細のアプリ内 URL と条件付きアンカーを修正する

内部 RPC の公開メソッド許可は明示的な allow-list に寄せ、追加した管理操作を Collector 側から任意呼出しできないようにする。

## Decisions

- SQS の個別メッセージ削除を取消手段にしない。receipt handle は受信時にしか得られず、標準キュー全体の探索でも完全性を保証できないため
- 全体初期化でもジョブ行と監査を削除しない。重複排除、原因調査、再投入対象の選択に必要なため
- 完全初期化だけは Event Store とタスクストアを削除する。復旧可能性は事前バックアップと manifest で確保する
- SQS purge だけに依存せず、DB 世代と lease token の二重フェンシングを使う。SQS/Lambda は重複配送を許容するため
- 取消は協調的取消とする。外部サイトへの送信など Worker が既に始めた副作用そのものは停止できないが、取消後の状態更新は受理しない

## Acceptance criteria

- Ready タスクを取り消すと `Cancelled` になり、既存通知を受信しても Worker は実行しない
- Running タスクを取り消すと旧 lease token の complete/fail/requeue は拒否される
- 取消と競合する更新があれば 409 になり、意図しない状態上書きをしない
- 取り消した一件を再投入すると世代が一度だけ進み、新世代の通知が一件 outbox に作られる
- 再投入前の古い通知と再投入後の新しい通知が逆順に届いても、新しい通知だけが acquire できる
- 全体初期化で成功済みを除く収集タスクが `Cancelled` となり、未配送 outbox、SQS 本体、DLQ が初期化対象になる
- 初期化中に作られた新規タスクは purge に巻き込まれず、安定化後に配送される
- 初期化処理は多重起動せず、Api 再起動後に安全に再開できる
- 個別取消、個別再投入、全体初期化に操作者、理由、時刻、結果の監査が残る
- ローカルの SQS 未設定環境でも論理取消と UI/API のテストができる
- 完全初期化はバックアップが検証済みの場合だけ実行され、完了後の Event Store とタスクストアは最新スキーマかつデータ 0 件になる
- 完全初期化後、レース、出走、人馬、予想、メモ、収集状態、ジョブ、marker、outbox の旧データを API から取得できない
- 完全初期化中の更新 API は maintenance response を返し、途中再起動後も工程を安全に再開できる
- 完全初期化の manifest から対象 DB、実行者、理由、時刻、バックアップパス、各工程結果を確認できる
- `/collection-tasks/{id}` と `/jobs/{id}` から一覧、親、子へ遷移しても重複パスにならない
- 存在しない詳細 section へのリンクは表示されない
- 既存の世代なし通知を処理でき、DB マイグレーション後も既存タスク詳細を表示できる
- 関連 unit/integration tests、solution build、`terraform fmt -check`、`terraform validate`、`git diff --check` が成功する

## Delivery plan

1. 世代カラム、初期化状態、Store 契約とマイグレーション互換処理を追加する
2. 通知、outbox、acquire、lease 完了処理へ世代フェンシングを追加する
3. 個別取消・理由付き再投入 API と監査を追加する
4. dispatcher 一時停止、SQS/DLQ purge、再開可能な全体初期化を追加する
5. バックアップ、maintenance mode、再開可能な収集 DB 完全初期化を追加する
6. 管理 UI の個別操作、二種類の初期化パネル、詳細リンク修正を追加する
7. Terraform IAM と復元手順を含む関連文書を更新し、受け入れ基準を検証する

コミットはデータモデル／フェンシング、個別操作、キュー初期化、DB 完全初期化、リンク修正、UI、インフラ・文書を、それぞれ単独検証可能な目的ごとに分ける。

## Verification record

- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`: 成功
- `dotnet build HorseRacingPrediction.sln --no-restore -c Release`: 成功。既存管理画面の未使用フィールドに関する `CS0414` 警告 14 件のみ
- `dotnet test HorseRacingPrediction.sln --no-build -c Release`: 成功
  - Application 56、Infrastructure 10、Domain 94、Api 81、Agents 177、Contracts 38、MachineLearning 14、Collector 85
- `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --no-build -c Release`: 成功、85 件
- `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --no-build -c Release`: 成功、81 件
- `git diff --check`: 成功。Windows checkout の LF/CRLF 変換予告のみ
- `terraform -chdir=infra/collector-lambda fmt -check` / `validate`: 実行環境に Terraform CLI がないため未実行。変更は既存 IAM policy の action/resource 追加に限定し、HCL の既存整形へ合わせた

追加した回帰テストでは、旧投入世代通知の拒否、実行中タスク取消後の lease 完了拒否、タスク DB のバックアップ・空スキーマ再作成を確認した。

## Deviations and follow-up

- 本提案では「バージョン遅れ」をタスク投入世代の遅れとして扱う。Lambda のデプロイ済みコードバージョン自体の整理を意図している場合は別スコープとして設計を更新する
- AWS SQS の件数メトリクスは近似値のため、初期化完了表示は AWS の即時 0 件観測ではなく、purge 成功と 60 秒の安定化完了を基準にする
- 完全初期化は Event Store の手動補正・予想・メモも削除する。収集由来データだけを選別削除するモードは、イベント間参照を壊すため本変更には含めない
- Terraform CLI が利用可能な CI またはデプロイ環境で `terraform fmt -check` と `terraform validate` を再実行する
