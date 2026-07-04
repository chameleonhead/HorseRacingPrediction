# Collector 設計

## 位置づけ

`HorseRacingPrediction.Collector` は、JRA 公式サイトから開催・出馬表・結果・払戻・馬・騎手・調教師情報を機械的スクレイピングで収集し、Api へ登録する専用プロセスである。

全体構成は [system-architecture.md](system-architecture.md) を参照。本ドキュメントは旧 `docs/agent-client-implementation-plan.md` のジョブモデル・状態管理の検討内容のうち、Collector の責務として現在実装済み・採用しているものを整理したものである。

## 責務境界

- **やること**: JRA サイトの巡回、構造化データの抽出、Api への冪等登録、失敗時の再試行
- **やらないこと**: 予想生成、SNS 投稿文生成（いずれも Predictor の責務、[predictor-design.md](predictor-design.md)）
- **LLM 利用**: 原則使わない。ページ遷移・抽出は機械的ロジックのみで行う（理由: [system-architecture.md](system-architecture.md) の LLM 利用方針）

### 例外: RaceTextInsightCollector（既定オフ）

任意情報源（展望・馬場傾向・注目馬コメントなど自由記述中心のページ）から `WebBrowserAgent`（LLM）を使って情報を収集し、Memo として Api に登録する機能が実装として存在する（`Scheduling/RaceTextInsightCollector.cs`）。

- `AgentProcessingOptions.EnableTextInsightCollection` で有効・無効を切り替え可能
- Collector の `appsettings.json` では **既定で `false`**（無効）
- LLM 呼び出しコストの問題により、現時点では運用対象外という位置づけとする。有効化する場合は、クエリテンプレート数 × 対象レース数に比例して LLM 呼び出しが増える点に注意する

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
| `ProcessingStateStore` | SQLite ベースのジョブ・マーカー永続化（`ProcessingJobEntity`, `ProcessingMarkerEntity`, `ProcessingStateDbContext`） |
| `RaceDataCollectionState` / `RaceDataCollectionStatusEntity` / `RaceDataCollectionStatusReadModel` | レース単位の収集状態 |
| `ResultDayCollectionState` / `ResultDayCollectionStatusEntity` / `ResultDayCollectionStatusReadModel` | 日単位の結果収集完了状態 |
| `RaceDataCollectionErrorCode` / `RaceDataCollectionErrorDescriptor` / `RaceDataCollectionErrorClassifier` | 失敗要因の分類 |

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

`appsettings.json` の `AgentProcessing` セクションで、以下の主要項目を制御する。詳細な既定値はコード（`AgentClient/Scheduling/AgentProcessingOptions.cs`）を参照。

- 収集系: `ScrapingIntervalMinutes`, `CollectionExecutionIntervalMinutes`, `CollectionBatchSize`, `CollectionLeaseMinutes`
- 結果収集対象範囲: `ResultLookbackDays`, `InitialResultBackfillYears`, `LiveResultLookbackDays`, `PreRaceResultLookbackDays`, `ResultLookaheadDays`
- 過去データ補完: `HistoricalRequestExecutionIntervalMinutes`, `HistoricalRequestBatchSize`, `HistoricalRequestLeaseMinutes`, `HistoricalRequestMaxAttempts`
- 機能フラグ: `EnableScheduleCollection`, `EnableRaceCardCollection`, `EnableRaceResultCollection`, `EnableTextInsightCollection`（既定 false）
- 同時実行制御: `MaxConcurrentJobs`（既定 1。単一実行制御はジョブ種別ごとではなくグローバルなリースで保証する）

## 今後の課題（未着手・要検討）

以下は旧ドキュメントで検討していたが、現時点では未着手または方針未確定の項目。着手する場合は本ドキュメントを更新すること。

- ジョブ永続ストアを Collector 専用 SQLite から Api 側集中管理へ移行するかどうか
- 地方競馬など JRA 以外のデータソースを Provider として追加する場合の抽象化
- `RaceTextInsightCollector` を有効化する場合のコスト対効果の再評価
