# Collector の Lambda 対応アーキテクチャ案

> 2026-08-26 方針確定: ジョブコントローラーは Api が所有する。SQS は配送通知に限定し、タスク本文・状態・依存関係・リース・再試行の正本は Api のタスクストアとする。Collector は計画・ポーリング・一括取得を行わず、通知で指定された単一タスクを実行する Worker とする。

## 確定する制御境界

### Api（Job Controller）

- 定期計画を起動し、実行すべきタスクと依存関係を確定する
- タスク作成と同一トランザクションで dispatch outbox を記録する
- outbox dispatcher が SQS へ `taskId` 通知を送信する
- SQS の重複配送を前提に、lease token 付きで単一タスクを取得させる
- 完了・失敗・再試行結果を受け、必要な子タスクを作成する
- 管理画面からの再投入も同じ outbox 経路へ流す

### SQS

- タスク本文を正本として保持しない
- `{ taskId, jobType, deduplicationKey }` の配送通知だけを持つ
- visibility timeout と DLQ により Lambda 呼び出し失敗を吸収する
- Standard Queue の少なくとも1回配送を前提とし、重複排除は Api が担う

### Collector Worker

- SQS イベントからタスク識別子を受け取る
- Api から対象タスクを lease token 付きで取得する
- 1 invocation で1タスクだけ実行する
- 実行結果を Api へ返し、自身では次のタスクを選択・計画しない
- ローカル実行時も Api が返す1件の通知を同じ Worker へ渡す

### 整合性規則

1. タスクと outbox は同じ SQLite トランザクションで保存する。
2. SQS 送信成功後に outbox を dispatched にする。送信後の更新失敗は重複通知として許容する。
3. Worker の acquire は `Ready -> Running` の条件更新で lease token を発行する。
4. 完了・再投入は同じ lease token を要求し、期限切れ Worker の遅延更新を拒否する。
5. Lambda の残り時間が安全猶予未満ならタスクを開始しない。
6. Lambda lease は timeout より短くし、SQS visibility timeout は Lambda timeout より長くする。

## 結論

Collector を次の 3 つの責務に分ける。

1. **Api**: 収集タスク、リース、試行履歴、収集ステータスを永続化し、管理 API / 管理画面を提供する
2. **Collector Worker**: Api からタスクを取得し、JRA を収集して、結果を Api へ報告する。常駐機能や UI は持たない
3. **実行ホスト**: ローカルでは `BackgroundService`、AWS では EventBridge Scheduler から起動する Lambda とし、どちらも同じ Worker ユースケースを呼び出す

初期移行では SQS を必須にせず、**Api のタスクストアをキューの正本として Worker が pull する方式**を推奨する。現在の `ProcessingStateStore` のリース・重複排除・再試行という意味論を保ちやすく、ローカルと Lambda で実行経路を共通化できるためである。将来、同時実行数や流量が増えた場合のみ、SQS を配送通知として追加する。

## 目標構成

```text
                         ┌──────────────────────────────┐
 管理者 ─ Cookie 認証 ─▶│ Api                          │
                         │ ・管理画面                    │
                         │ ・Collection Tasks API       │
                         │ ・タスク/リース/履歴/状態 DB  │
                         └──────────────┬───────────────┘
                                        │ X-Api-Key
                         acquire/report │
                                        ▼
                  ┌────────────────────────────────────┐
                  │ Collector Worker（共通ユースケース）│
                  │ 1回で有限件を処理し、必ず終了する   │
                  └──────────────┬─────────────────────┘
                                 │
                   ┌─────────────┴─────────────┐
                   ▼                           ▼
          Local Worker Host             Lambda Container
          BackgroundService             EventBridge Scheduler
```

Api は Collector のスクレイピング実装を参照しない。共有するのはタスク契約 DTO と HTTP クライアントだけにする。

## 現状から移動する責務

| 現在の場所 | 移動先 | 補足 |
|---|---|---|
| `Collector/Scheduling/ProcessingStateDbContext.cs` | Api/Infrastructure | `jobs`, `markers`, `race_data_collection_statuses`, `agent_acquisition_statuses`, `result_day_collection_statuses` を Api 管理 DB に移す |
| `ProcessingStateStore` の永続化と照会 | Api/Application | acquire、完了、失敗、再投入、状態照会をユースケースとして公開する |
| `Agent*EndpointExtensions.cs` | Api | JSON API は `/api/admin/collection/*` と `/api/internal/collection/*` に分ける |
| Collector の Jobs/ResultDays/AcquisitionStatuses 画面 | Api の管理画面 | 既存 Api 管理画面のナビゲーションと Cookie 認証に統合する |
| `ScrapingRegistrationService` の計画ロジック | Api 側の planning endpoint/service | EventBridge またはローカル scheduler が有限の「計画1回」を呼ぶ |
| `CollectionExecutionService` / `HistoricalDataRequestExecutionService` のループ | Local Host のみ | ループ内部の1サイクルを共通 `CollectionWorker.RunAsync` に抽出する |
| JRA debug tools | 原則ローカル専用 | ブラウザーを直接操作するため Api 管理画面には移さない。必要なら開発環境限定の Collector Tool Host とする |

`Agent*` という名称は実態に合わせ、移行時に `CollectionTask*` / `Acquisition*` へ変更するのが望ましい。

## タスク API

Worker 用 API と人間向け管理 API を分ける。いずれも既存の `/api` 配下に置く。

### Worker 用（機械認証）

| メソッド/パス | 用途 |
|---|---|
| `POST /api/internal/collection/tasks/acquire` | 対象 job type、最大件数、worker id、lease duration を指定して原子的に取得する |
| `POST /api/internal/collection/tasks/{taskId}/heartbeat` | 長い収集処理のリースを延長する |
| `POST /api/internal/collection/tasks/{taskId}/complete` | 完了。生成した子タスクも同一要求で登録可能にする |
| `POST /api/internal/collection/tasks/{taskId}/fail` | retryable、retryAt、error code/message を報告する |
| `PUT /api/internal/collection/statuses/races/{raceKey}` | レース収集状態を冪等更新する |
| `PUT /api/internal/collection/statuses/result-days/{dayKey}` | 日次収集状態を冪等更新する |
| `PUT /api/internal/collection/statuses/acquisitions/{key}` | プロフィール等の取得状態を冪等更新する |

acquire のレスポンスには `taskId`, `jobType`, `payload`, `attemptCount`, `leaseToken`, `leaseExpiresAt` を含める。完了・失敗・heartbeat は `leaseToken` が現在値と一致する場合だけ受理し、期限切れ Worker が新しい実行結果を上書きしないようにする。

子タスクの登録と親タスク完了は Api の単一トランザクションで処理する。現状の「月探索から日探索を登録する」処理などで、子タスク登録後・親完了前の通信断による不整合を避けられる。各タスクは `(jobType, deduplicationKey)` の一意制約を維持する。

### 管理用（管理画面から自己 HTTP 呼び出し）

| メソッド/パス | 用途 |
|---|---|
| `GET /api/admin/collection/tasks` | 一覧・フィルタ・ページング |
| `GET /api/admin/collection/tasks/{taskId}` | payload、全試行、エラー、リース情報 |
| `POST /api/admin/collection/tasks/{taskId}/requeue` | 強制再投入 |
| `POST /api/admin/collection/result-days/trigger` | 日次収集の手動投入 |
| `GET /api/admin/collection/result-days` | 日次状況 |
| `GET /api/admin/collection/races` | レース単位状況 |
| `GET /api/admin/collection/acquisitions` | 取得状況 |

管理画面は Api の既存 `AdminApiClient` 経由でこれらを呼び、`/collection/tasks`, `/collection/result-days`, `/collection/acquisitions` に配置する。Collector の画面をほぼ移植できるが、再実行操作は Api が所有するため Lambda の稼働有無に依存しない。

## Worker の共通実行モデル

常駐する `BackgroundService` を実処理の中心にせず、次のような有限実行インターフェースを中心にする。

```csharp
public interface ICollectionWorker
{
    Task<CollectionRunResult> RunAsync(
        CollectionRunRequest request,
        CancellationToken cancellationToken);
}
```

`CollectionRunRequest` には最大件数と deadline を渡す。Worker は deadline の 60～90 秒前には新しいタスクの取得を止め、処理中タスクの結果報告とブラウザー終了に時間を残す。

- **ローカル**: `BackgroundService` が一定間隔で `RunAsync` を呼ぶ。`dotnet run` と Docker Compose のどちらでも実行可能
- **Lambda**: handler が Lambda context の残り時間から deadline を算出し、`RunAsync` を1回だけ呼んで終了
- **手動検証**: CLI host から `--once --max-tasks 1` のように同じ処理を実行

タスク取得・完了等は `ProcessingStateStore` の直接 DB 操作から `ICollectionTaskClient` に置き換える。Worker のテストでは in-memory fake を使い、Api 側ではストアの競合・リースを統合テストする。

## AWS 構成

### 推奨する初期構成

- Lambda は Playwright/Chromium を含む **コンテナイメージ**として ECR へ配置する
- EventBridge Scheduler が planning 用 Lambda と worker 用 Lambda を定期起動する（同一バイナリに event mode を渡してもよい）
- reserved concurrency は当初 `1` とし、JRA への負荷と現行 `MaxConcurrentJobs = 1` を維持する
- Lambda timeout は 15 分、Worker の内部 deadline は 13 分程度にする
- `/tmp` はブラウザーの一時ファイル専用とし、状態の正本にはしない
- API キーなどは Secrets Manager または SSM Parameter Store から注入し、イメージや設定ファイルへ含めない
- CloudWatch Logs に `taskId`, `jobType`, `deduplicationKey`, `attempt`, `workerId` を構造化出力する

Lambda は1回最大15分で、実行環境のローカル状態を呼び出し間の永続化に使えない。そのため、現在の30分リースをそのまま使うのではなく、タスクの最大所要時間を計測し、Lambda用には10～12分程度のリース＋heartbeatを基本とする。1タスクが安定して Lambda の期限内に終わらない場合、そのタスクをページ/レース単位に分割する。分割不能なら当該ワーカーだけ ECS Fargate に残す。

### SQS を初期構成に入れない理由

Api DB と SQS の二重状態を最初から導入すると、タスク作成とメッセージ送信の原子性（outbox）、再送、管理画面上の状態との整合を追加で解決する必要がある。現状は同時実行1かつ小規模なので、EventBridge 起動の Worker が Api から acquire するだけで十分である。

次の条件が出たら SQS を追加する。

- 待ち時間を Scheduler 間隔より短くしたい
- 複数 Lambda へ大きく並列化したい
- Api 障害中も配送要求をバッファしたい

その場合もタスク本文とステータスの正本は Api に残し、SQS には `taskId` の通知だけを載せる。Api は transactional outbox に記録し、dispatcher が SQS へ送る。Lambda/SQS は少なくとも1回配送なので、Api の lease token と冪等更新を引き続き必須とする。

## データベース方針

Api が単一インスタンスで稼働する現段階では、収集タスクテーブルを既存 Api DB と同じ永続ボリューム上に置くことは可能である。ただし Lambda から SQLite ファイルを直接共有してはならず、必ず Api 経由にする。

将来 Api 自体を複数インスタンス化する、または可用性を上げる場合は PostgreSQL（RDS/Aurora）への移行を先に行う。acquire は `SELECT ... FOR UPDATE SKIP LOCKED` 相当、または条件付き UPDATE で実装し、同じタスクを複数 Worker が取得しないことを保証する。イベントストアと収集運用テーブルは同一 DB サーバーでも、DbContext/スキーマとマイグレーションを分離する。

タスク本体とは別に `collection_task_attempts` を追加し、各試行の開始/終了、worker、エラーを追記型で保持する。現在の `jobs.last_error` だけより管理画面と障害調査に適する。

## Lambda 適合性の検証項目

Playwright を採用しているため、設計だけで Lambda 適合を確定しない。最初に以下のスパイクを行う。

1. Lambda互換コンテナで Chromium が起動し、JRA の代表ページへ遷移できる
2. cold start、1レース、1日探索、プロフィール取得の p50/p95 実行時間と最大メモリを測る
3. `/tmp` 使用量、Chromiumプロセス終了、ファイルディスクリプタ数を確認する
4. Lambda終了直前の cancellation でタスクがリース切れ後に再取得できる
5. 同じタスクを2回実行しても Api のドメイン書き込みと状態更新が冪等である

2026年8月時点で AWS の .NET 8 Lambda ベースイメージは 2026年11月10日に非推奨予定と案内されているため、実装着手時には .NET 10 への更新も同時に評価する。

## 段階的な移行手順

### Phase 1: 契約と Api 側状態管理

1. 収集タスク DTO/enum を Collector から依存方向が逆転しない共有プロジェクトへ移す
2. Api に Collection Operations 用 DbContext、migration、store、internal/admin endpoints を追加
3. Collector 画面を Api 管理画面へ移し、Api側ストアを表示・操作する
4. 既存 SQLite からの一度限りの移行ツールを用意する（稼働中タスクは Ready に戻す）

### Phase 2: ローカル Worker の API 化

1. `ProcessingStateStore` 依存を `ICollectionTaskClient` に置換
2. 3つの `BackgroundService` から1サイクルの planning/worker ユースケースを抽出
3. Collector を UI なしの Worker Host に変更
4. Docker Compose で Api + Local Worker の回帰確認

この時点で本番をローカル/常駐ホストのまま運用でき、Lambda移行とは独立して責務分割の効果を得られる。

### Phase 3: Lambda スパイクと導入

1. Lambda handler project とコンテナ Dockerfile を追加
2. AWS SAM または CDK で ECR/Lambda/EventBridge/IAM/Logs を定義
3. representative tasks の性能計測を行い、メモリ、timeout、batch sizeを決定
4. reserved concurrency 1 で本番並行検証し、ローカル Worker を停止

### Phase 4: 必要時のみスケール

タスク種別ごとの Lambda 分離、SQS + outbox、PostgreSQL、並列数増加を計測結果に基づいて導入する。

## 受け入れ条件

- Api停止中でも Worker がローカル SQLite へ勝手にフォールバックせず、安全に失敗する
- ローカル Host と Lambda が同じ `ICollectionWorker` とスクレイピング実装を使う
- タスク/試行/レース/日次/取得ステータスが Api 管理画面だけで確認できる
- 管理画面から再投入したタスクを、次回のローカルまたはLambda実行が取得できる
- Worker の異常終了後、リース期限を過ぎれば別 Worker がタスクを再取得できる
- 重複呼び出し、期限切れ完了報告、同時 acquire のテストがある
- 1 Lambda invocation が終了猶予を残して完了し、15分を超える処理が存在しないか、分割/Fargate対象として明示されている

## 採用判断

この構成で進めることを推奨する。ただし、Lambda採用の最終判断は Playwright スパイクの p95 が期限内に十分収まり、JRA側のセッションをタスク単位で再生成しても運用可能であることを確認してから行う。適合しない処理が一部だけなら、Api中心のタスク管理と共通 Worker は維持し、実行ホストだけ Fargate に差し替えられる。
