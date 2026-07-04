# Predictor 設計

## 位置づけ

`HorseRacingPrediction.Predictor` は、Api から取得したデータのみを入力に予想を生成し、その予想結果をもとに SNS 投稿用メッセージをマルチエージェントで生成するプロセスである。

全体構成・LLM 利用方針は [system-architecture.md](system-architecture.md) を参照。

Predictor の責務は 2 フェーズに分かれる。

1. **予想生成フェーズ**（実装済み・LLM 不使用）— `ApiOnlyPredictionWorkflow`
2. **投稿文生成フェーズ**（新規・マルチエージェント LLM 使用）— 本ドキュメントで設計する

## フェーズ1: 予想生成（ML / API ベース、LLM 不使用）

### 方針

予想生成は Api の ReadModel（`RacePredictionContext`）と ML 予測（`GetMlPredictionAsync`）のみを入力とし、LLM は使わない。

理由: 予想生成はレース・出走馬ごとに高頻度で実行されるため、LLM チェーンを挟むと呼び出しコストが運用上の制約になる（[system-architecture.md](system-architecture.md) 参照）。

### 実装: `ApiOnlyPredictionWorkflow`

`src/HorseRacingPrediction.Predictor/Scheduling/ApiOnlyPredictionWorkflow.cs`

処理順序:

1. `IRaceQueryService.GetRacePredictionContextAsync` でレースコンテキスト（出走馬一覧など）を取得する
2. `IRaceQueryService.GetMlPredictionAsync` で ML.NET による予測順位を取得する
3. ML 予測が取得できた場合はその順位・スコアをそのまま採用し、取得できない場合は出馬番号順の暫定順位にフォールバックする
4. `CreatePredictionTicket`（`predictorType: "ApiOnlyPredictor"`）で予想票を作成する
5. 各出走馬に `AddPredictionMark`（◎○▲△☆）と `AddPredictionRationale`（ML スコアを根拠として記録）を追加する
6. `FinalizePredictionTicket` で確定する

### 実行制御: `PredictionExecutionService`

- `ProcessingStateStore` の予想キューから対象レースを取り出す（`TakeReadyPredictionCandidatesAsync`）
- `BlockPredictionWhileHistoricalRequestsPending` が true の場合、Collector 側の過去データ補完要求（`HistoricalDataRequestTracker`）が未完了なら予想を保留し再キューする
- 成功したら `MarkPredictionCompletedAsync`、失敗したら理由付きで再キューする

### 廃止方針: 旧 LLM 主導予想ワークフロー

以下は非推奨とする。予想生成の新規実装・改修はすべて `ApiOnlyPredictionWorkflow` に対して行う。コード自体の削除は別タスクとして扱う。

- `HorseRacingPrediction.Agents.Workflow.PredictionWorkflow`
- `HorseRacingPrediction.Agents.Agents.RaceContextAgent`
- `HorseRacingPrediction.Agents.Agents.HorseAnalysisAgent`
- `HorseRacingPrediction.Agents.Agents.PredictionAgent`

## フェーズ2: SNS 投稿文生成（新規・マルチエージェント LLM）

### 目的

確定した予想票（`PredictionTicket` + `PredictionMark` + `PredictionRationale`）を入力に、X などの SNS に投稿するためのメッセージを生成する。

### 実行頻度とコストの考え方

予想票の確定ごとに 1 回程度の実行を想定する（レース数 × 数エージェント呼び出し）。フェーズ1（予想生成）が出走馬単位・高頻度で LLM を使わないのに対し、フェーズ2は低頻度かつ「表現生成」という LLM の強みが活きる領域であるため、ここに限定してマルチエージェント LLM を採用する。

### エージェント構成（視点分担型）

1つの投稿文を、異なる視点を担当する複数エージェントが並行に草稿を作り、統合エージェントが1つの投稿文にまとめる構成（Parallelization → 統合、[.github/skills/agent-design](../.github/skills/agent-design/SKILL.md) の Orchestrator-workers に近いパターン）を採る。

| エージェント | 入力 | 出力 | 役割 |
|---|---|---|---|
| `HonmeiCommentaryAgent`（本命解説） | ◎本命馬の `PredictionMark` + `PredictionRationale` | 短文コメント | 本命馬を推す理由を簡潔に言語化する |
| `AnaCommentaryAgent`（穴馬解説） | ▲単穴・△連下の `PredictionMark` + `PredictionRationale` | 短文コメント | 妙味のある馬・注目ポイントを言語化する |
| `DataRationaleAgent`（データ根拠） | ML 予測スコア・過去成績など数値的根拠 | 短文コメント | 数値的根拠を簡潔に要約する |
| `PostComposerAgent`（統合） | 上記3エージェントの出力 + 媒体制約（文字数・体裁） | 投稿用テキスト（媒体別） | 3つの草稿を1つの投稿文に統合し、文字数調整・ハッシュタグ選定を行う |

- `HonmeiCommentaryAgent` / `AnaCommentaryAgent` / `DataRationaleAgent` は並行実行する（互いに依存しない）
- `PostComposerAgent` は3エージェントの出力が揃った後に実行する（Prompt chaining の最終ステップに相当）
- 各エージェントは [.github/skills/agent-design](../.github/skills/agent-design/SKILL.md) の基本構造（`ChatClientAgent` ラッパー、1ファイル1クラス、`AgentName` / `SystemPrompt` 定数）に従って実装する

### ワークフロー

```
PredictionTicket (確定済み)
        │
        ├──▶ HonmeiCommentaryAgent ──┐
        ├──▶ AnaCommentaryAgent ─────┼──▶ PostComposerAgent ──▶ 投稿用テキスト
        └──▶ DataRationaleAgent ─────┘
```

新規ワークフロークラス（例: `PostGenerationWorkflow`）を `HorseRacingPrediction.Agents/Workflow/` に追加し、3エージェントの並行実行後に統合エージェントへ結果を渡す。既存の `PredictionWorkflow`（Prompt chaining のみ）とは異なり、並行実行 + 統合のステップを持つ点が構造上の差分になる。

### 入力データの取得

投稿文生成エージェントに渡す予想票データは、既存の `RaceQueryTools` / `IRaceQueryService` を再利用して取得する想定とする（予想票確定後の `PredictionTicket` を読み取り専用で参照するツールが必要であれば追加する）。

### 出力の保存先（要検討事項）

生成した投稿文の保存先は未確定。以下いずれかを検討する。

- 既存の `Memo` 集約（`RaceId` に紐づく自由記述として保存できる）を再利用する
- 予想票専用の新しい ReadModel／付随情報として保持する

いずれの場合も、Api への書き込みは冪等にし、同一予想票に対する再生成を許容する設計にする。

### スコープ外（明示）

- 実際に X などの SNS へ API 経由で投稿する処理は、本ドキュメントの対象外とする
- 投稿文生成までを Predictor の責務とし、投稿の実行（認証・レート制限・投稿失敗時の扱いなど）は別途スコープを切って検討する

## 今後の実装ステップ

1. `PredictionTicket` 確定後に投稿文生成を起動するトリガー（`PredictionExecutionService` 完了後のフック、または別のポーリングサービス）を追加する
2. `HonmeiCommentaryAgent` / `AnaCommentaryAgent` / `DataRationaleAgent` / `PostComposerAgent` を実装する
3. 生成結果の保存先を決定し、書き込みサービスを追加する
4. 媒体別（X 以外の SNS を追加する場合）のバリアント生成方針を `PostComposerAgent` のプロンプトに反映する
