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

## フェーズ2: SNS 投稿文生成（実装済み・ストーリー仕立て・マルチエージェント LLM）

### 目的

確定した予想票（`PredictionTicket` + `PredictionMark` + `PredictionRationale`）を入力に、X などの SNS に投稿する「ストーリー仕立て」のメッセージを生成する。単なる予想印の列挙ではなく、読み手が最後まで読みたくなる一つの物語として組み立てる点がフェーズ2の要件である。

### 実行頻度とコストの考え方

予想票の確定ごとに 1 回程度の実行を想定する（レース数 × 数エージェント呼び出し）。フェーズ1（予想生成）が出走馬単位・高頻度で LLM を使わないのに対し、フェーズ2は低頻度かつ「表現生成」という LLM の強みが活きる領域であるため、ここに限定してマルチエージェント LLM を採用する。

### ストーリー構成（起承転結）

`StoryPostComposerAgent` は、3エージェントの草稿を単純結合するのではなく、以下の4段構成（起承転結）を持つ一つの物語として再構成する。

| 段 | 内容 | 主な入力ソース |
|---|---|---|
| 起（舞台設定） | レース名・条件・見どころを短く提示し読み手を引き込む | `RaceQueryTools.GetRacePredictionContext` |
| 承（本命の掘り下げ） | 本命馬（◎）を推す理由をデータと絡めて掘り下げる | `HonmeiCommentaryAgent` + `DataRationaleAgent` の草稿 |
| 転（視点の転換） | 穴馬・対抗目線を提示し、意外性・妙味で物語に転換を加える | `AnaCommentaryAgent` の草稿 |
| 結（結論・CTA） | ◎宣言と一言、必要なら購入検討を促す一文で締める | 予想票の確定印一覧 |

文字数上限・ハッシュタグ方針は `PostGenerationOptions`（Predictor 側設定）で制御し、`StoryPostComposerAgent` のプロンプトへ実行時に注入する。

### エージェント構成（視点分担型 → ストーリー統合）

1つの投稿文を、異なる視点を担当する複数エージェントが並行に草稿を作り、統合エージェントが起承転結の物語1本にまとめる構成（Parallelization → 統合、[.github/skills/agent-design](../.github/skills/agent-design/SKILL.md) の Orchestrator-workers に近いパターン）を採る。

| エージェント | 入力 | 出力 | 役割 |
|---|---|---|---|
| `HonmeiCommentaryAgent`（本命解説） | ◎本命馬の `PredictionMark` + `PredictionRationale` | 短文コメント | 本命馬を推す理由を簡潔に言語化する |
| `AnaCommentaryAgent`（穴馬解説） | ▲単穴・△連下の `PredictionMark` + `PredictionRationale` | 短文コメント | 妙味のある馬・注目ポイントを言語化する |
| `DataRationaleAgent`（データ根拠） | ML 予測スコア・過去成績など数値的根拠 | 短文コメント | 数値的根拠を簡潔に要約する |
| `StoryPostComposerAgent`（ストーリー統合） | 上記3エージェントの出力 + レースコンテキスト + 媒体制約（文字数・体裁） | 投稿用テキスト（起承転結） | 3つの草稿を起承転結の物語1本に再構成し、文字数調整・ハッシュタグ選定を行う |

- `HonmeiCommentaryAgent` / `AnaCommentaryAgent` / `DataRationaleAgent` は並行実行する（互いに依存しない）
- `StoryPostComposerAgent` は3エージェントの出力が揃った後に実行する（Prompt chaining の最終ステップに相当）
- 各エージェントは [.github/skills/agent-design](../.github/skills/agent-design/SKILL.md) の基本構造（`ChatClientAgent` ラッパー、1ファイル1クラス、`AgentName` / `SystemPrompt` 定数）に従って実装する

### ワークフロー: `PostGenerationWorkflow`

```
PredictionTicket (確定済み)
        │
        ├──▶ HonmeiCommentaryAgent ──┐
        ├──▶ AnaCommentaryAgent ─────┼──▶ StoryPostComposerAgent ──▶ 投稿用テキスト（起承転結）
        └──▶ DataRationaleAgent ─────┘
```

`src/HorseRacingPrediction.Agents/Workflow/PostGenerationWorkflow.cs` に実装する。3エージェントを `Task.WhenAll` で並行実行し、揃った草稿を `StoryPostComposerAgent` に渡す構造上、既存の `PredictionWorkflow`（`WorkflowBuilder` による Prompt chaining のみ）とは異なり、並行実行 + 統合のステップを持つ。

### 入力データの取得

投稿文生成エージェントに渡すデータは、既存の `RaceQueryTools` / `IRaceQueryService` を再利用して取得する。確定済み `PredictionTicket`（印・スコア・コメント）を参照するため、`IRaceQueryService.GetPredictionTicketAsync` / `RaceQueryTools.GetPredictionTicket` を追加した。

### 出力の保存先: `Memo` 集約（決定）

生成した投稿文は既存の `Memo` 集約に保存する。

- `MemoType`: `"SnsStoryPost"`
- `Subjects`: `[{ SubjectType: "Race", SubjectId: raceId }]`
- `MemoId`: `memo-post-{predictionTicketId}`（決定論的 ID。同一予想票に対する再生成は同じ `MemoId` を指す）

書き込みは `IMemoWriteService.CreateOrUpdateRaceMemoAsync` で行う。まず作成を試み、`409 Conflict`（`MemoId` が既に存在＝再生成）の場合は更新にフォールバックする。これにより Api への書き込みは冪等になり、同一予想票に対する再生成を許容する。この create→conflict→update フォールバックを機能させるため、Api 側 `POST /api/memos` は他の集約（Horse/Jockey/Trainer/Race）と同様に「既に作成済み」の `InvalidOperationException` を `409 Conflict` へ変換するよう修正した。

### 起動トリガー: `PredictionExecutionService` のフック

`ApiOnlyPredictionWorkflow` 自体は LLM 不使用の原則を保つ。`PredictionExecutionService.RunOneCycleAsync` が予想票の確定（`FinalizePredictionTicketAsync` 相当）に成功した直後に `PostGenerationWorkflow.RunAsync(predictionTicketId)` を呼び出す。投稿文生成が失敗しても予想自体は成功済みのため、別の try/catch でラップしログ警告のみに留め、予想の再キューには影響させない。

### スコープ外（明示）

- 実際に X などの SNS へ API 経由で投稿する処理は、本ドキュメントの対象外とする
- 投稿文生成までを Predictor の責務とし、投稿の実行(認証・レート制限・投稿失敗時の扱いなど)は別途スコープを切って検討する

## 実装状況

フェーズ1・フェーズ2ともに実装済み。Predictor プロセスは `HorseRacingPrediction.Agents` を参照し、`PostGenerationOptions`（`appsettings.json` の `PostGeneration` セクション）で有効化・文字数上限・ハッシュタグを制御する。IChatClient の実体は `LMStudioChatClient`（`LMStudio` セクションで接続先を設定）を使用する。
