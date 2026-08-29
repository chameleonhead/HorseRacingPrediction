# Collector 設計

## 位置づけ

> 2026-08-23: 収集タスクの永続化・照会 API・管理画面は Api 側へ移し、Collector はローカル常駐または `--once` で動く Worker に変更した。Lambda は `Dockerfile.collector-lambda` の同じ `--once` 経路を使用する。

`HorseRacingPrediction.Collector` は、JRA 公式サイトから開催・出馬表・結果・払戻・馬・騎手・調教師情報を機械的スクレイピングで収集し、Api へ登録する専用プロセスである。バックグラウンドでの収集処理に加え、その収集バッチ処理の状況を確認・操作するための Web UI / API も自身で提供する。

全体構成は [system-architecture.md](system-architecture.md) を参照。本ドキュメントは旧 `docs/agent-client-implementation-plan.md` のジョブモデル・状態管理の検討内容のうち、Collector の責務として現在実装済み・採用しているものを整理したものである。

> 旧ジョブ実行クライアントの HTTP クライアント、ジョブ状態管理、収集バッチ、関連テストは Collector へ移管済みである（2026-07-08）。AI エージェント、LLM 呼び出し、任意テキスト収集、予想実行は Collector の責務から外している。

## 責務境界

- **やること**: JRA サイトの巡回、構造化データの抽出、Api への冪等登録、失敗時の再試行
- **やらないこと**: 予想生成、SNS 投稿文生成（いずれも Predictor の責務、[predictor-design.md](predictor-design.md)）
- **LLM 利用**: 使わない。AI エージェント、`HorseRacingPrediction.Agents` 参照、`Microsoft.Extensions.AI` 依存は持たず、ページ遷移・抽出は機械的ロジックのみで行う（理由: [system-architecture.md](system-architecture.md) の LLM 利用方針）

## JRA スクレイピング制約（必須）

- URL の推測・生成・手組みは行わない
- ページ遷移は必ずブラウザー操作（クリック・フォーム入力・戻る/進む）で行う
- href 解析からの遷移再構築は行わない
- 実装詳細は [jra-site-data-collector.md](jra-site-data-collector.md)（`JraSiteDataCollector` によるセッション保持型ナビゲーション）と [jra-page-map-blueprint.md](jra-page-map-blueprint.md)（ページ判定・構造化抽出の責務分割）を参照

## コンポーネント構成（実装済み）

### 収集トリガー・実行

| クラス | 役割 |
|---|---|
| `ScrapingRegistrationService` | 開催予定・出馬表・結果収集ジョブの投入を定期実行する |
| `CollectionExecutionService` | 投入済み収集ジョブを取り出して実行する |
| `HistoricalDataRequestExecutionService` | 過去成績・プロフィール補完要求を実行する |
| `CollectionExecutionTrigger` | 収集実行の即時トリガー |

### 過去データ補完

| クラス | 役割 |
|---|---|
| `IJraResultDateDiscoveryService` / `JraResultMonthDateDiscoveryService` | 月単位で未取得の結果日付を発見する |
| `IHistoricalRaceReferenceCollector` / `JraHistoricalRaceReferenceCollector` | 出走馬の過去レース参照を収集する |
| `IJraRaceResultLookup` / `JraSiteDataCollectorRaceResultLookup` | `JraSiteDataCollector` 経由でレース結果を参照する |
| `IHistoricalRaceResultCollector` / `JraHistoricalRaceResultCollector` | 過去レース結果を収集し Api へ登録する |
| `IJraProfileLookup` / `JraSiteDataCollectorProfileLookup` | 馬・騎手・調教師のプロフィールを参照する |
| `IHistoricalDataRequestHandler` / `JraHistoricalDataRequestHandler` | 過去データ補完要求を処理する |
| `HistoricalDataRequestPlanner` | 補完要求の計画を立てる |

### 状態管理

| クラス | 役割 |
|---|---|
| `ProcessingStateStore` | Api が所有する SQLite ベースのジョブ・マーカー永続化。契約と実装は `HorseRacingPrediction.CollectionOperations` から共有する |
| `RaceDataCollectionState` / `RaceDataCollectionStatusEntity` / `RaceDataCollectionStatusReadModel` | レース単位の収集状態 |
| `ResultDayCollectionState` / `ResultDayCollectionStatusEntity` / `ResultDayCollectionStatusReadModel` | 日単位の結果収集完了状態 |
| `RaceDataCollectionErrorCode` / `RaceDataCollectionErrorDescriptor` / `RaceDataCollectionErrorClassifier` | 失敗要因の分類 |

### 収集状況の監視・操作（Api の Web UI / API）

Api が収集バッチ処理の状況を確認・操作する Minimal API をホストする。収集タスク操作は API 限定とし、Api 管理画面には表示しない。Collector は `IProcessingStateStore` の HTTP proxy を通して Api の正本を更新する。ローカルDBは `UseApiStateStore=false` を明示したテスト・開発用途に限る。

#### API エンドポイント（`Scheduling/Agent*EndpointExtensions.cs`）

| エンドポイント | 役割 |
|---|---|
| `GET /agent/job-statuses` | ジョブ一覧を JobType / Status で絞り込んで取得する |
| `GET /agent/jobs/{jobId}` | ジョブ詳細（ペイロード・エラー内容含む）を取得する |
| `GET /agent/result-day-statuses` | 日単位の結果収集状況を期間指定で取得する |
| `GET /agent/race-collection-statuses` | レース単位の収集状況を期間指定で取得する（`IRaceQueryService` でレース名を補完） |
| `GET /agent/acquisition-statuses` | 馬・騎手・調教師・馬主のプロフィール取得状況を期間・種別で取得する |
| `POST /agent/job-statuses/{jobType}/{deduplicationKey}/requeue` | 指定ジョブを強制再キューする |
| `POST /agent/result-day-statuses/{providerType}/{targetDate}/requeue` | 日単位の収集を Discovery/Collection モードで再投入する |
| `POST /agent/result-day-jobs/trigger` | 任意の日付・プロバイダで日次収集を新規投入する |

これらは旧ジョブ実行クライアントの `AgentDashboardEndpointExtensions` / `AgentCollectionStatusEndpointExtensions` / `AgentAcquisitionStatusEndpointExtensions` を移管したものである。ただし `/agent/prediction-jobs/trigger`（予想ジョブ投入）は移管していない。Collector と Predictor は別々の SQLite ジョブストア（`collector-processing-jobs.db` / `predictor-processing-jobs.db`）を使うため、Collector からの予想ジョブ投入は Predictor 側に反映されず意味を持たないためである。

#### Blazor Server 画面（`Web/Components/Pages/`）

| パス | 画面 | 内容 |
|---|---|---|
| `/jobs` | ジョブ一覧 | JobType/Status フィルタ、再キュー、日次収集の新規投入 |
| `/jobs/{JobId}` | ジョブ詳細 | ペイロード・エラー内容の確認、再キュー |
| `/result-days` | 日次収集状況 | Discovery/Collection 単位の再投入 |
| `/acquisition-statuses` | 取得ステータス | 馬・騎手・調教師・馬主のプロフィール取得結果一覧 |
| `/jra-tool` | JRA URL 抽出ツール（デバッグ） | 任意 URL のページ種別判定・JSON 抽出結果を確認 |
| `/snapshot-tool` | PageSnapshot ビューア（デバッグ） | 任意 URL の `PageSnapshot` をセクション構造で確認 |

JRA デバッグツール群は `JraTesting/`（`JraJsonExtractionService` など）に実装されており、Collector 自身のスクレイピング・抽出ロジックの動作確認に使う。

### ジョブペイロード種別（実装済み）

- `RaceCardCollectionJobPayload` — 出馬表収集
- `RaceResultCollectionJobPayload` — レース結果収集
- `ResultBackfillPlanningRequestPayload` — バックフィル計画
- `ResultMonthDiscoveryRequestPayload` — 月単位の未取得日探索
- `ResultDayDiscoveryRequestPayload` — 日単位の開催・レース確定
- `ResultDayCollectionRequestPayload` — 日単位の収集実行
- `HistoricalRaceResultCollectionRequestPayload` — 過去レース結果の個別収集
- `HorseHistoryCollectionRequestPayload` / `JockeyHistoryCollectionRequestPayload` — 馬・騎手の履歴補完

これは旧 `agent-client-implementation-plan.md` で検討していたジョブ分解方針（月探索→日探索→日次収集→レース収集）が、Collector 側の実装として採用されたものである。

## 実行モード（Live / PreRace / Idle）

`AgentWorkModeResolver` が、開催日程・当日判定・リード日数（`PreRaceLeadDays`）から実行モードを決定する。

| モード | 条件 | 想定動作 |
|---|---|---|
| `Live` | 本日が開催日 | リアルタイム抽出を優先 |
| `PreRace` | 開催が `PreRaceLeadDays` 以内に迫っている | 出馬表・過去成績補完を優先 |
| `Idle` | それ以外 | バックフィルを優先 |

## 主要設定（`AgentProcessingOptions`）

`appsettings.json` の `AgentProcessing` セクションで、以下の主要項目を制御する。詳細な既定値はコード（`Scheduling/AgentProcessingOptions.cs`）を参照。

- 収集系: `ScrapingIntervalMinutes`, `CollectionExecutionIntervalMinutes`, `CollectionBatchSize`, `CollectionLeaseMinutes`
- 結果収集対象範囲: `ResultLookbackDays`, `InitialResultBackfillYears`, `LiveResultLookbackDays`, `PreRaceResultLookbackDays`, `ResultLookaheadDays`
- 過去データ補完: `HistoricalRequestExecutionIntervalMinutes`, `HistoricalRequestBatchSize`, `HistoricalRequestLeaseMinutes`, `HistoricalRequestMaxAttempts`
- 機能フラグ: `EnableScheduleCollection`, `EnableRaceCardCollection`, `EnableRaceResultCollection`
- 同時実行制御: `MaxConcurrentJobs`（既定 1。単一実行制御はジョブ種別ごとではなくグローバルなリースで保証する）

## 今後の課題（未着手・要検討）

Lambda 対応と、タスク状態・管理画面を Api 側へ移す具体案は [lambda-collector-architecture.md](lambda-collector-architecture.md) を参照。

以下は旧ドキュメントで検討していたが、現時点では未着手または方針未確定の項目。着手する場合は本ドキュメントを更新すること。

- ジョブ永続ストアを Collector 専用 SQLite から Api 側集中管理へ移行する（段階的移行案を上記文書で検討済み）
- 地方競馬など JRA 以外のデータソースを Provider として追加する場合の抽象化
- Collector が所有する `ProcessingStateStore` / `AgentProcessingOptions` / HTTP クライアント実装を、将来的に専用共有ライブラリへ分離するかどうか
