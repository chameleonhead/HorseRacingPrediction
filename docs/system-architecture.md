# システム構成とサービス責務

## 位置づけ

このドキュメントは、旧 `docs/agent-scenario.md` と `docs/agent-client-implementation-plan.md` を置き換える。
両ドキュメントは「AI エージェントが収集・予想を主導する」構成を前提に書かれていたが、実装が進む中で **LLM 呼び出しコストの問題** が明確になったため、現在は以下の3サービス構成・責務分担に再設計している。

Collector 側の詳細は [collector-design.md](collector-design.md)、Predictor 側の詳細は [predictor-design.md](predictor-design.md) を参照。API・ドメインモデルは [domain-design.md](domain-design.md) を参照。

Collector のローカル/Lambda共通実行と、収集タスク・管理画面の Api 集約案は [lambda-collector-architecture.md](lambda-collector-architecture.md) を参照。

## 方針転換の背景

| # | 当初の前提 | 現在の前提 | 理由 |
|---|---|---|---|
| 1 | AI エージェントが JRA サイトを自律探索して収集する | 機械的スクレイピングに統一する | 再現性・監査性・再試行制御を優先するため（[collector-design.md](collector-design.md)） |
| 2 | 予想生成も LLM 主導のマルチエージェント（RaceContextAgent → HorseAnalysisAgent → PredictionAgent）で行う | 予想生成は ML.NET + API データのみで行い、LLM は使わない | 予想は高頻度（レース毎・出走馬毎）に実行されるため、LLM 呼び出しコストが運用上のボトルネックになる |
| 3 | 投稿文整形は「補助的にAIを使える」程度の位置づけ | 予想確定後の SNS 投稿文生成に、マルチエージェント LLM を明確に採用する | 投稿文生成は 1 予想票あたり数回程度の低頻度処理であり、LLM の「自然な文章表現」という強みが活きる領域 |
| 4 | 旧ジョブ実行クライアントが収集・予想・投稿を一体で担う | Collector（収集）と Predictor（予想・投稿文生成）に分離する | 責務単位でプロセスを分離し、スケジュールや障害影響範囲を独立させる（直近コミットで実施済み） |

## サービス構成

```
┌────────────────┐         ┌────────────────┐         ┌────────────────┐
│    Collector    │──HTTP──▶│       Api       │◀──HTTP──│    Predictor    │
│  JRA機械的収集   │X-Api-Key│ CQRS+ES データ管理 │X-Api-Key│ 予想 + 投稿文生成 │
│  LLM不使用       │         │                 │         │ (ML→予想／LLM→投稿文)│
└────────────────┘         └────────────────┘         └────────────────┘
```

### Api

- レース・馬・騎手・調教師・予想票・結果・払戻を CQRS + Event Sourcing で管理する
- 書き込みはコマンドエンドポイント、読み取りは用途別 ReadModel で提供する
- 機械間通信（Collector / Predictor）向けの JSON API は例外なく `/api` 配下に置き、`X-Api-Key` ヘッダーで認証する（ML 予測系の `/api/races/{raceId}/ml-prediction`, `/api/ml/train` を含む）
- Collector と同様に、自身で Blazor Server 製の管理画面（`/races`, `/horses`, `/jockeys`, `/trainers`, `/predictions` などルート直下）をホストする（`Microsoft.NET.Sdk.Web`。旧読み取り UI を移管・拡張し、2026-07-07 に単なる参照ツールから馬・騎手・調教師の登録／編集／別名統合／データ訂正、レース・予想票のデータ訂正、メモの CRUD ができるメンテナンスツールへ変更）
  - JSON API が常に `/api` 配下、管理UIが常にルート直下という規約により両者のパスは重ならないため、認証免除の判定は管理UIのルート名（`/races`, `/horses` など）を明示的に列挙するだけでよい（`Security/ApiKeyApplicationBuilderExtensions.cs`）
  - 管理画面は Cookie 認証で保護する。ログイン画面（`/login`）はユーザー名固定「user」、パスワードは `ApiKey:Key`（JSON API と同じ値）で認証する
  - 管理画面はコマンド/クエリを直接実行するのではなく、既存の JSON API を自己ループバック HTTP で呼び出す（`Web/ApiBrowsing/AdminApiClient`）。ここでも自プロセス自身の `X-Api-Key` を自動付与する
  - メンテナンスは既存 API コマンドの範囲内に限定し、レース・予想票の新規作成やライフサイクル進行（出走登録・結果確定など）は対象外（Collector / Predictor の自動処理が担う）
- 詳細: [domain-design.md](domain-design.md), [automation-design.md](automation-design.md)

### Collector

- JRA 公式サイトを Playwright による機械的スクレイピングで巡回し、Api へ収集データを登録する
- ページ遷移・抽出処理に LLM は使わず、AI エージェントや `Microsoft.Extensions.AI` 依存も持たない
- 収集タスクの正本と管理画面は Api が所有し、Collector は HTTP 経由でタスクを取得・更新する
- ローカル常駐モードと `--once` の有限実行モードを持ち、Lambda コンテナも後者を使用する
- 詳細: [collector-design.md](collector-design.md)

### Predictor

- Api から取得した `RacePredictionContext` / ML 予測のみを入力に予想票を作成・確定する（LLM は使わない）
- 確定した予想票をもとに、SNS 投稿文をマルチエージェント LLM ワークフローで生成する
- 詳細: [predictor-design.md](predictor-design.md)

### 旧ジョブ実行クライアント

- 廃止済み。HTTP クライアント、ジョブ状態管理、収集バッチ、関連テストは Collector へ移管した（2026-07-08）
- Predictor と JraVerifier は、移管済みの HTTP / Scheduling 補助型を利用するため Collector を参照する
- AI エージェント、任意テキスト収集、Microsoft Agent Framework の DevUI ホストは Collector へ移管しない

## プロジェクト依存関係

Api は Collector / Predictor を参照しない。Collector は収集実行と旧共有補助型（HTTP クライアント、ジョブ状態管理、Scheduling DTO）を所有し、Predictor と JraVerifier はその補助型を利用するため Collector を参照する。Predictor はフェーズ2（SNS 投稿文生成）のために `HorseRacingPrediction.Agents` も参照する。

```
HorseRacingPrediction.Api ──────────┐
                                     ▼
HorseRacingPrediction.Collector ──▶ HorseRacingPrediction.ApiClient ──▶ HorseRacingPrediction.Contracts
          ▲                          ▲                                          ▲
          │                          │                                          │
HorseRacingPrediction.Predictor ─────┴──────────────────────▶ HorseRacingPrediction.Agents
```

- `Contracts`: `RacePredictionContextReadModel` / `HorseReadModel` / `RaceStatus` / `PredictionTicketSummaryReadModel` などの読み取り用 DTO のみ。他プロジェクトへの参照を持たない
- `ApiClient`: `IRaceQueryService` / `IPredictionWriteService` / `IDataCollectionWriteService` などのクライアント側インターフェースと `DataCollectionWriteTools` を持つ。`Contracts` を参照する
- `Api` は自身のエンドポイント用 DTO（`Api/Contracts/`）を別途持つが、クライアントと共有する読み取り用 DTO は `Contracts` を参照して再利用し、二重定義しない
- Collector・Predictor は HTTP（`X-Api-Key` 付き）でのみ Api と通信する。Predictor から Collector への参照は、移管済み補助型（HTTP クライアント、`IMemoWriteService`）の利用に限る
- `Agents` は `RaceQueryTools`（`IRaceQueryService` 経由）を通じて Api のデータを読み取るのみで、書き込みは Predictor 側が `IMemoWriteService` で行う（Agents は Collector を参照しない）

## LLM 利用方針（最重要の前提変更）

| サービス | 処理 | LLM 利用 | 実行頻度の目安 | 理由 |
|---|---|---|---|---|
| Collector | JRA ページ遷移・構造化抽出 | 使わない | レース・開催ごとに高頻度 | 再現性・監査性・コストを優先 |
| Predictor | 予想生成（順位・印・信頼度・根拠） | 使わない（ML.NET + API データのみ） | レース・出走馬ごとに高頻度 | LLM 主導3エージェント構成は呼び出しコストの観点で不採用に変更し、`ApiOnlyPredictionWorkflow` に一本化する |
| Predictor | 予想確定後の SNS 投稿文生成 | 使う（マルチエージェント・視点分担型） | 予想票確定ごとに低頻度（1回程度） | 低頻度かつ表現生成が主目的であり、LLM の強みが活きる |

## データフロー

1. Collector が JRA を巡回し、開催・出馬表・結果・払戻を Api へ登録する
2. Predictor が Api の `RacePredictionContext` と ML 予測を取得し、`ApiOnlyPredictionWorkflow` のみで予想票を作成・確定し、Api へ書き込む
3. Predictor が確定した予想票をもとに、マルチエージェント SNS 投稿文生成ワークフローを実行し、投稿用テキストを生成する
4. 生成された投稿文の実際の SNS への投稿（X API 連携など）は今回のスコープ外とする。当面は生成結果を確認のうえ手動投稿する運用を前提とする

## 廃止・非推奨とする既存コンポーネント

以下は、予想生成を ML/API ベースに一本化する方針により非推奨とする。コード自体の削除は本ドキュメント更新の範囲外の別タスクとして扱う。

- `HorseRacingPrediction.Agents.Workflow.PredictionWorkflow`（LLM 主導の3ステップ予想ワークフロー）
- `HorseRacingPrediction.Agents.Agents.RaceContextAgent`
- `HorseRacingPrediction.Agents.Agents.HorseAnalysisAgent`
- `HorseRacingPrediction.Agents.Agents.PredictionAgent`

今後の予想生成は `HorseRacingPrediction.Predictor` の `ApiOnlyPredictionWorkflow` に一本化する。
