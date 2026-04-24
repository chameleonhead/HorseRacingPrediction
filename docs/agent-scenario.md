# エージェントシナリオとアーキテクチャ

## 概要

エージェントはサービスプログラムとして任意の PC 上で動作し、クラウド上のデータサーバー（API）と API キーで通信しながら、木曜から日曜にかけての競馬予測サイクルを自動実行する。

---

## システム構成

```
┌──────────────────────────────────────┐
│  エージェント PC（ローカルサービス）      │
│                                      │
│  WeeklyScheduleWorkflow              │
│  ├─ WeekendRaceDiscoveryAgent        │
│  ├─ DataCollectionWorkflow           │
│  │   ├─ RaceDataAgent                │
│  │   ├─ HorseDataAgent               │
│  │   ├─ JockeyDataAgent              │
│  │   └─ StableDataAgent              │
│  ├─ PostPositionPredictionAgent      │
│  └─ PredictionWorkflow               │
│      ├─ RaceContextAgent             │
│      ├─ HorseAnalysisAgent           │
│      └─ PredictionAgent              │
│                                      │
│  WebBrowserAgent（Playwright）        │
└────────────────┬─────────────────────┘
                 │  X-Api-Key（HTTPS）
                 ▼
┌──────────────────────────────────────┐
│  クラウド（データサーバー）               │
│                                      │
│  HorseRacingPrediction.Api           │
│  ├─ CQRS コマンドエンドポイント          │
│  ├─ クエリ（ReadModel）エンドポイント    │
│  └─ ML 予測エンドポイント               │
│                                      │
│  HorseRacingPrediction.MachineLearning│
│  └─ IRacePredictor（ML.NET）          │
└──────────────────────────────────────┘
```

### 通信方式

| 方向 | プロトコル | 認証 |
|------|-----------|------|
| エージェント → サーバー（書き込み） | HTTPS REST | `X-Api-Key` ヘッダー |
| エージェント → サーバー（読み込み） | HTTPS REST | `X-Api-Key` ヘッダー |
| ML 予測（サーバーサイド） | サーバー内部呼び出し | — |

---

## 週次スケジュール

```
月  火  水  木          金              土              日
               │          │              │              │
               ▼          ▼              ▼              ▼
           DiscoverRaces  PostPositions  RaceDay(土)    RaceDay(日)
           CollectData    +Predict初回   CollectData    CollectData
           (定期更新)     (定期更新)     Predict(1h前)  Predict(1h前)
                                        CollectResult  CollectResult
                                        Evaluate       Evaluate
```

---

## フェーズ詳細

### フェーズ 1：木曜（レース発見 + データ収集）

**目的**: 今週末のレースを特定し、過去情報・基本情報を収集する。

```
WeeklyScheduleWorkflow.DiscoverRacesAsync()
  └─ WeekendRaceDiscoveryAgent
       ├─ CalendarTools.GetCurrentDateTime / GetWeekendDates
       └─ WebBrowserAgent（JRA 公式・netkeiba）
            → [WeekendRaceInfo] レース一覧（レース名・競馬場・出走馬・騎手・調教師）

WeeklyScheduleWorkflow.CollectDataAsync()  ← 数時間おきに繰り返し実行
  └─ DataCollectionWorkflow（複数レースを並列実行）
       ├─ RaceDataAgent    → レース基本情報・過去傾向（Markdown）
       ├─ HorseDataAgent   → 出走馬の戦績・血統・適性（Markdown）
       ├─ JockeyDataAgent  → 騎手の成績・傾向（Markdown）
       └─ StableDataAgent  → 厩舎・調教師の傾向（Markdown）
            ↓
       DataCollectionWriteTools → サーバー API（SourceDocument 保存）
```

---

### フェーズ 2：金曜（枠順確定 + 初回予測）

**目的**: 枠順確定後にデータを再収集し、ML 予測と AI 予測を組み合わせた初回予測を作成する。

```
WeeklyScheduleWorkflow.CollectPostPositionsAndPredictAsync()
  └─ [各レース並列]
       ├─ DataCollectionWorkflow.CollectAsync()  ← 枠番を含む最新データ再収集
       │
       └─ PostPositionPredictionAgent
            入力: レース情報 + 馬情報 + 騎手情報 + 厩舎情報
            分析: 枠番有利不利 / 脚質展開 / コース適性 / 騎手相性 / 厩舎仕上がり
            出力: 予測レポート（◎○▲△ + 推奨馬券）Markdown

     ＊ ML 予測はサーバーサイドで実行（IRacePredictor）
        エージェントは ML 結果を参照して最終予測を調整できる
```

定期的に再実行することで予測を更新する。

---

### フェーズ 3：土曜・日曜（当日予測 + 結果収集）

**目的**: レース 1 時間前に最新情報で予測を更新し、レース後に結果を収集・評価する。

#### 当日の定期データ更新（継続）

```
WeeklyScheduleWorkflow.CollectDataAsync()  ← 当日も定期実行
  └─ DataCollectionWorkflow
       └─ RaceDataAgent など
            → 馬場状態・天候・オッズ変動・前日比の調教情報
```

#### レース 1 時間前：予測実行

```
PredictionWorkflow.RunAsync(raceId)
  ├─ Step 1: RaceContextAgent
  │    ツール: RaceQueryTools（ReadModel 参照）+ WebFetchTools
  │    出力: レースコンテキスト（最新馬場・天候・オッズ）
  │
  ├─ Step 2: HorseAnalysisAgent
  │    ツール: RaceQueryTools + WebFetchTools
  │    入力: Step 1 の出力
  │    出力: 各馬の総合評価
  │
  └─ Step 3: PredictionAgent
       ツール: RaceQueryTools + PredictionWriteTools + WebFetchTools
       入力: Step 1〜2 の出力
       出力: PredictionTicket（DB 保存）+ 予測サマリー
```

#### レース後：結果収集と評価

```
JraRaceResultCollectionWorkflow
  ├─ JraResultUrlDiscoveryAgent → 結果ページ URL 特定
  ├─ JraRaceCardScraper        → Playwright で結果スクレイピング
  └─ DataCollectionWriteTools  → RaceResult / EntryResult / PayoutResult 保存

評価: EvaluatePredictionTicket コマンド発行
  └─ PredictionEvaluation（的中区分・ROI）を PredictionTicket に記録
```

---

## エージェント一覧

| エージェント | 役割 | パターン |
|------------|------|---------|
| `WeekendRaceDiscoveryAgent` | 週末レース一覧を発見、JSON で返す | 構造化出力（C） |
| `RaceDataAgent` | レース基本情報・過去傾向を収集 | テキスト出力（A） |
| `HorseDataAgent` | 出走馬の戦績・血統・適性を収集 | テキスト出力（A） |
| `JockeyDataAgent` | 騎手の成績・傾向を収集 | テキスト出力（A） |
| `StableDataAgent` | 厩舎・調教師の成績・傾向を収集 | テキスト出力（A） |
| `PostPositionPredictionAgent` | 枠順確定後の予測レポート生成 | テキスト出力（A） |
| `RaceContextAgent` | レースコンテキスト（馬場・天候）収集 | テキスト出力（A） |
| `HorseAnalysisAgent` | 各馬の総合評価 | テキスト出力（A） |
| `PredictionAgent` | 予測票の作成・DB 保存 | テキスト出力（A） |
| `WebBrowserAgent` | Playwright を使ったブラウザ操作 | Agent-as-Tool（B） |

---

## ツール一覧

| ツール | 提供する機能 | 使用エージェント |
|-------|------------|---------------|
| `PlaywrightTools` | ブラウザプリミティブ（検索・ナビゲート・リンク取得） | WebBrowserAgent |
| `WebFetchTools` | 自然言語での Web 検索委譲 | データ収集系エージェント全般 |
| `CalendarTools` | 現在日時・週末日付の取得 | WeekendRaceDiscoveryAgent |
| `RaceQueryTools` | EventFlow ReadModel クエリ | RaceContextAgent, HorseAnalysisAgent, PredictionAgent |
| `PredictionWriteTools` | 予測票の作成・確定 API 呼び出し | PredictionAgent |
| `DataCollectionWriteTools` | SourceDocument 保存 API 呼び出し | データ収集系ワークフロー |

---

## ワークフロー一覧

| ワークフロー | ステップ | 実行パターン |
|------------|---------|------------|
| `WeeklyScheduleWorkflow` | Discover → CollectData → Predict | Orchestrator（週次スケジュール管理） |
| `DataCollectionWorkflow` | Race + Horse + Jockey + Stable 収集 | Parallelization（4 エージェント並列） |
| `PredictionWorkflow` | RaceContext → HorseAnalysis → Prediction | Prompt chaining（3 ステップ順次） |
| `JraRaceCardCollectionWorkflow` | URL 発見 → スクレイピング → 保存 | Prompt chaining |
| `JraRaceResultCollectionWorkflow` | URL 発見 → スクレイピング → 保存 | Prompt chaining |

---

## ML との役割分担

| 処理 | 実行場所 | 方式 |
|------|---------|------|
| 統計モデルによる着順予測 | サーバーサイド | `IRacePredictor`（ML.NET FastTree） |
| 情報収集・自然言語処理 | エージェント（ローカル） | LLM（ChatClientAgent） |
| 枠順・展開・印判断 | エージェント（ローカル） | `PostPositionPredictionAgent` |
| 最終予測票の作成 | エージェント（ローカル） | `PredictionWorkflow` |

エージェントは ML 予測を `RaceQueryTools` 経由でサーバーから取得し、AI 予測の根拠の一つとして活用する。

---

## レースライフサイクルとエージェント処理の対応

```
Race ライフサイクル          エージェント処理
─────────────────────────────────────────────
Draft                     ← WeekendRaceDiscoveryAgent がレース登録
CardPublished             ← JraRaceCardCollectionWorkflow が出馬表登録
PreRaceOpen               ← PredictionWorkflow が PredictionTicket 作成
InProgress                ← 発走（自動遷移）
ResultDeclared            ← JraRaceResultCollectionWorkflow が結果登録
PayoutDeclared            ← 払戻登録
Closed                    ← EvaluatePredictionTicket で評価完了
```
