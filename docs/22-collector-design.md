# Collector 設計

## 位置づけ

> 2026-08-23: 収集タスクの永続化・照会 API・管理画面は Api 側へ移し、Collector はローカル常駐または `--once` で動く Worker に変更した。Lambda は `Dockerfile.collector-lambda` の同じ `--once` 経路を使用する。

`HorseRacingPrediction.Collector` は、API が計画したジョブを取得し、JRA 公式サイトから開催・出馬表・結果・払戻・馬・騎手・調教師情報を機械的スクレイピングで収集し、結果を API へ報告する専用サービスである。Web UI、HTTP API、ジョブ計画、永続的な状態ストアは持たない。

全体構成は [00-system-architecture.md](00-system-architecture.md) を参照。本ドキュメントは旧 `docs/agent-client-implementation-plan.md` のジョブモデル・状態管理の検討内容のうち、Collector の責務として現在実装済み・採用しているものを整理したものである。

> 旧ジョブ実行クライアントの HTTP クライアント、ジョブ状態管理、収集バッチ、関連テストは Collector へ移管済みである（2026-07-08）。AI エージェント、LLM 呼び出し、任意テキスト収集、予想実行は Collector の責務から外している。

## 責務境界

- **やること**: JRA サイトの巡回、構造化データの抽出、Api への冪等登録、失敗時の再試行
- **やらないこと**: 予想生成、SNS 投稿文生成（いずれも Predictor の責務、[25-predictor-design.md](25-predictor-design.md)）
- **LLM 利用**: 使わない。AI エージェント、`HorseRacingPrediction.Agents` 参照、`Microsoft.Extensions.AI` 依存は持たず、ページ遷移・抽出は機械的ロジックのみで行う（理由: [00-system-architecture.md](00-system-architecture.md) の LLM 利用方針）

## JRA スクレイピング制約（必須）

対策ありサイトへの遷移ルール（URL 推測禁止、ブラウザー操作必須など）は `.github/skills/scraper-development/SKILL.md` を正とする。実装詳細は [23-jra-scraping-redesign.md](23-jra-scraping-redesign.md)（`JraSession`/`JraNavigator`/`JraPageReader`/`IJraPage` によるページ遷移・判定・構造化抽出の設計）を参照。

## コンポーネント構成（実装済み）

### 収集トリガー・実行

Apiの /api/admin/races/{raceId}/reacquisition がRaceReacquisitionジョブを登録する。同一対象の実行中依頼は原子的に重複抑止し、監査とoutboxを保存する。CollectionExecutionServiceの常駐・--once両経路で指定レースを再取得する。元ジョブや日別取得状態には依存しない。未公開は待機、部分失敗は失敗として扱う。[決定と検証](changes/20260908_race-detail-reacquisition/README.md)を参照。

| クラス | 役割 |
|---|---|
| `ScrapingRegistrationService` | 開催予定・出馬表・結果収集ジョブの投入を定期実行する |
| `CollectionExecutionService` | 投入済み収集ジョブを取り出して実行する |
| `HistoricalDataRequestExecutionService`（旧経路） | 現行CollectorではDI登録が無効で、旧補完要求を実行しない |
| `CollectionExecutionTrigger` | 収集実行の即時トリガー |

### タイムアウトと実行保留

`CollectionTaskRunner`が単発・常駐・計画ジョブの有効リース、個別保留、内部デッドラインとジョブ制限時間を監視する。タイムアウトは失敗試行と収集全体停止を同時保存し、Readyへ戻さない。ホストの通常終了による中断は再投入、個別保留による中断はキャンセルした試行として記録する。

個別保留は既存の実行状態とは独立した`IsHeld`で永続化する。Running中はキャンセル要求済みを表し、Workerは1秒間隔（照会のタイムアウト3秒）で確認する。ブラウザー等の後始末後に有効リースで中断応答すると、実行状態をReadyへ戻し、IsHeldを維持して保留を確定する。保留解除だけがIsHeldを解除し、新しい配送世代を作る。旧リース/通知の結果は新しい実行に適用しない。

保留中の親を依存解消で完了させず、解除時に子の結果を再集計する。親・子への保留の連鎖は行わない。スケジュール再登録、watchdog、リラン、重複依頼でも個別保留を維持する。全体停止中も実行中Workerの状態照会・結果報告を受け付ける。DBの追加列は既存行を非保留として移行し、データと履歴を保持する。[設計と検証](changes/20260908_collection-timeout-hold/README.md)を参照。

### 過去データ補完

馬・調教師の /api/admin/subjects/{kind}/{id}/collection/profile と馬の collection/history がSubjectProfileRefresh / HorseHistoryDiscoveryを登録する。履歴探索の親ジョブが、全ページからHorseHistoryRace子ジョブを作る。地方・海外等はHorseHistoryExcludedとして理由を保存し配送しない。探索完了を永続チェックポイントに記録し、再開・再試行は成功済み子を維持して失敗分だけ投入する。新規依頼では保存済みレースも再更新する。常駐・単発とも稼働中CollectionExecutionServiceが実行し、下表の旧補完Workerには依存しない。[決定と検証](changes/20260908_subject-refresh-horse-history/README.md)を参照。

| クラス | 役割 |
|---|---|
| `IJraResultDateDiscoveryService` / `JraResultMonthDateDiscoveryService` | 月単位で未取得の結果日付を発見する |
| `IHistoricalRaceReferenceCollector` / `NoOpHistoricalRaceReferenceCollector` | 現行DIは常に空の参照を返す暫定実装。過去レース結果の自動補完要求は登録されない |
| `IJraRaceResultLookup` / `JraSiteDataCollectorRaceResultLookup` | `JraSiteDataCollector` 経由でレース結果を参照する |
| `IHistoricalRaceResultCollector`（旧経路） | 実行実装のDI登録は無効 |
| `IJraProfileLookup` / `JraSiteDataCollectorProfileLookup` | 馬・騎手・調教師のプロフィールを参照する |
| `IHistoricalDataRequestHandler`（旧経路） | ハンドラーのDI登録は無効 |
| `HistoricalDataRequestPlanner` | 出馬表収集後に旧プロフィール等の補完要求を条件付き登録する。手動の馬起点履歴探索には未接続 |

#### 出馬表の自動更新期間（提案）

2026-09-09提案: 出馬表の自動取得は、未発走かつ公式結果未確認のレースだけを対象とする。初回成功後も出走取消・騎手変更・馬体重などへ追随するため発走前の限定的な更新は維持するが、公式発走時刻到達後または確定着順・払戻の取得成功後はレース単位で「出馬表取得終了」とし、出馬表詳細へ再訪しない。発走時刻到達は公式のレース確定を意味せず、結果側はJRAの確定情報を取得するまで別に再試行する。開催日の全レースが出馬表取得終了、または日別結果状態が完了した場合は、成功済み日付ジョブを定期計画で再登録しない。手動のレース再取得はこの自動停止条件とは分離する。詳細と受け入れ基準は [発走時刻到達後の出馬表自動取得を停止する](changes/20260909_stop-redundant-racecard-collection/README.md) を参照。承認前のため現行動作は変わらない。

2026-09-08調査: 上記の旧自動経路と、稼働中の手動 `HorseHistoryDiscovery` / `HorseHistoryRace` は別経路である。過去データの自動抽出停止と旧要求の実行停止が併存している。[調査とタイムアウト・保留の変更案](changes/20260908_collection-timeout-hold/README.md)を参照。自動経路の復旧や既存データの一括再登録は未実施。

2026-09-09提案: 過去レースの自律取得は、旧 URL 列挙 Worker の復活ではなく、現行の `RaceResultCollection` を使う日付単位のローリングバックフィルへ統合する。直近5日より前を既定3年まで新しい順に進め、月をチェックポイント、開催日を実行単位とする。さらに、出馬表または収集済みJRAレースから判明した馬を、稼働中の `SubjectProfileRefresh` / `HorseHistoryDiscovery` へ自動接続する。今週末の出馬表を最優先、その全出走予定馬の公式プロフィール・履歴探索・不足している直近5走を次順位とし、開催週につき1回更新する。全掲載履歴の残りも取得対象だが、当日結果を塞がない順位で継続する。通常・過去レース由来の未取得馬も各優先度と件数上限で補完するが、履歴レースから別馬の全履歴へ無制限に高優先度展開しない。既定15分ごとの永続 `AcquisitionPlanReview` ジョブが不足・滞留・進捗・失敗・キュー状態を再評価し、未実行ジョブの優先度と次回投入量を調整する。開催中と週末向け前景ジョブ滞留中は長期バックフィルを抑制する。見直しは実行中リース、停止、保留、DeadLetterを自動解除せず、基盤のリース・配送修復は既存watchdogに委ねる。状態、再試行、導入手順を含む設計は [過去レースと馬公式情報の自律収集](changes/20260909_autonomous-historical-race-backfill/README.md) を参照。承認前のため未実装であり、現行動作は変わらない。

### 状態管理

| クラス | 役割 |
|---|---|
| `ProcessingStateStore` | Api が所有する SQLite ベースのジョブ・マーカー永続化。契約と実装は `HorseRacingPrediction.CollectionOperations` から共有する |
| `RaceDataCollectionState` / `RaceDataCollectionStatusEntity` / `RaceDataCollectionStatusReadModel` | レース単位の収集状態 |
| `ResultDayCollectionState` / `ResultDayCollectionStatusEntity` / `ResultDayCollectionStatusReadModel` | 日単位の結果収集完了状態 |
| `RaceDataCollectionErrorCode` / `RaceDataCollectionErrorDescriptor` / `RaceDataCollectionErrorClassifier` | 失敗要因の分類 |

### 収集状況の監視・操作（Api の Web UI / API）

API が収集バッチ処理の状況を確認・操作する Minimal API と管理画面をホストする。Collector は API のジョブ契約を通して正本を更新する。旧来の `UseApiStateStore` 設定および Collector ローカルDB経路は削除済みであり、開発・テストでも API 所有の状態ストアまたは専用のテストダブルを使用する。

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

これらは旧ジョブ実行クライアントの `AgentDashboardEndpointExtensions` / `AgentCollectionStatusEndpointExtensions` / `AgentAcquisitionStatusEndpointExtensions` を API 側へ移管したものである。ただし `/agent/prediction-jobs/trigger`（予想ジョブ投入）は移管していない。予想ジョブ投入は Predictor 側の責務であり、Collector から操作しない。

#### 管理画面

Collector は Blazor Server 画面、Web Host、静的資産、通常運用向け HTTP endpoint を持たない。収集ジョブ、日別状況、データ取得状況、停止・再開、リラン、再取得は API 管理画面を正本とする。旧 `/collection-tasks` は API 管理画面側で `/jobs` へリダイレクトする。

JRA 抽出サービス `JraTesting/JraJsonExtractionService` は、Collector 内部のスクレイピング補助サービスとして残す。任意 URL をブラウザから操作する管理画面や HTTP endpoint は提供しない。

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
| `PreRace` | 開催が `PreRaceLeadDays` 以内に迫っている | 今週末の出馬表・出走予定馬の公式プロフィールと全掲載履歴を優先 |
| `Idle` | それ以外 | バックフィルを優先 |

## 主要設定（`AgentProcessingOptions`）

`appsettings.json` の `AgentProcessing` セクションで、以下の主要項目を制御する。詳細な既定値はコード（`Scheduling/AgentProcessingOptions.cs`）を参照。

- 収集系: `ScrapingIntervalMinutes`, `CollectionExecutionIntervalMinutes`, `CollectionBatchSize`, `CollectionLeaseMinutes`
- 結果収集対象範囲: `ResultLookbackDays`, `InitialResultBackfillYears`, `LiveResultLookbackDays`, `PreRaceResultLookbackDays`, `ResultLookaheadDays`
  - 自動登録の `ResultLookbackDays` は既定5日。JSTの当日〜5日前（両端を含む）の開催日を対象とし、日次収集完了済みの日付は再登録しない。
- 過去データ補完: `HistoricalRequestExecutionIntervalMinutes`, `HistoricalRequestBatchSize`, `HistoricalRequestLeaseMinutes`, `HistoricalRequestMaxAttempts`
- 自律収集見直し（提案）: 見直し有効化、見直し間隔（既定15分）、進捗停滞判定期間、低優先度投入上限
- 機能フラグ: `EnableScheduleCollection`, `EnableRaceCardCollection`, `EnableRaceResultCollection`
- 同時実行制御: `MaxConcurrentJobs`（既定 1。単一実行制御はジョブ種別ごとではなくグローバルなリースで保証する）

## 今後の課題（未着手・要検討）

Lambda 対応の詳細は [01-lambda-collector-architecture.md](01-lambda-collector-architecture.md) を参照。

以下は旧ドキュメントで検討していたが、現時点では未着手または方針未確定の項目。着手する場合は本ドキュメントを更新すること。

- 地方競馬など JRA 以外のデータソースを Provider として追加する場合の抽象化
