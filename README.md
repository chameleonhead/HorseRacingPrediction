# HorseRacingPrediction

CQRS+ES と ASP.NET Core を前提にした競馬予想アプリケーションの実装リポジトリです。
Api / Collector / Predictor の3サービス構成で、データ収集・予想生成・管理をそれぞれ独立したプロセスとして実装しています。

## サービス構成

```
┌────────────────┐         ┌────────────────┐         ┌────────────────┐
│    Collector    │──HTTP──▶│       Api       │◀──HTTP──│    Predictor    │
│  JRA機械的収集   │X-Api-Key│ CQRS+ES データ管理 │X-Api-Key│ 予想 + 投稿文生成 │
│  LLM不使用       │         │ + Blazor管理画面  │         │(ML→予想／LLM→投稿文)│
└────────────────┘         └────────────────┘         └────────────────┘
```

- **Api**（`src/HorseRacingPrediction.Api`）: レース・馬・騎手・調教師・予想票などを EventFlow による CQRS+ES で管理する ASP.NET Core アプリ。`/api` 配下の JSON API（`X-Api-Key` 認証）に加えて、ルート直下（`/races`, `/horses`, `/jockeys`, `/trainers`, `/predictions`, `/owners`, `/jobs` など）に Blazor Server 製の管理画面（Cookie 認証、Fluent UI Blazor）を自ホストする。
- **Collector**（`src/HorseRacingPrediction.Collector`）: JRA 公式サイトを Playwright で機械的に巡回し、Api へ収集データを登録する。LLM は使わない。
  - ⚠️ **現状**: JRA サイト構造の再設計（[docs/jra-scraping.md](docs/jra-scraping.md)）に伴い、実際の収集実行経路（`--once` / 常駐ワーカー）は一時的に無効化されている（`src/HorseRacingPrediction.Collector/Program.cs`）。プロセス自体は起動できるが、収集タスクは実行されない。
- **Predictor**（`src/HorseRacingPrediction.Predictor`）: Api から取得したデータと ML.NET モデルのみで予想票を作成・確定し（LLM 不使用）、確定後の SNS 投稿文をマルチエージェント LLM ワークフローで生成する（投稿自体はスコープ外、手動運用）。

詳細なサービス責務・依存関係は [docs/system-architecture.md](docs/system-architecture.md) を参照してください。

## 設計ドキュメント

- [docs/system-architecture.md](docs/system-architecture.md): Api / Collector / Predictor の3サービス構成と LLM 利用方針
- [docs/domain-design.md](docs/domain-design.md): CQRS+ES 前提の競馬予想ドメイン設計
- [docs/automation-design.md](docs/automation-design.md): 自動処理の責務設計
- [docs/collector-design.md](docs/collector-design.md): Collector（JRA機械的収集）の設計
- [docs/lambda-collector-architecture.md](docs/lambda-collector-architecture.md): Collector のローカル/Lambda共通実行と管理画面の Api 集約案
- [docs/predictor-design.md](docs/predictor-design.md): Predictor（ML予想 + SNS投稿文マルチエージェント生成）の設計
- [docs/jra-scraping.md](docs/jra-scraping.md): JRAスクレイピング層（`JraSession`/`JraNavigator`/`JraPageReader`/`IJraPage`）の設計指示書（現在進行中の作業）
- [docs/jra-html-change-diagnostics.md](docs/jra-html-change-diagnostics.md): JRA HTML 構造変更の診断手順
- [docs/admin-ui-design.md](docs/admin-ui-design.md): 管理サイト UI / UX とジョブ運用画面の設計
- [docs/design-guidelines.md](docs/design-guidelines.md): 管理画面のデザインガイドライン
- [docs/lightsail-deployment.md](docs/lightsail-deployment.md): 最安構成を優先した Lightsail デプロイ雛形
- [docs/changes/](docs/changes/): 個別機能の変更提案・実装記録

## 現時点の方針

- 収集対象はレース、馬、騎手、調教師、およびそれらに紐づく一次テキスト情報
- 正規化済みの構造化データと、原文・元データを両方保持する
- レース前の予想とレース後の結果を同一ライフサイクル上で管理する
- 書き込みモデルは CQRS + Event Sourcing を前提に設計する
- ASP.NET Core Web API と API キー認証を前提にする
- 予想は同一レースに複数登録できるようにする

## Api の実装状況

`src/HorseRacingPrediction.Api` に、EventFlow を利用した CQRS+ES アプリケーションを実装済みです。

- アーキテクチャ: CQRS + Event Sourcing（EventFlow 1.2.3）
- JSON API: ASP.NET Core Minimal API（`/api` 配下、`X-Api-Key` ヘッダー認証。`HORSE_RACING_API_KEY` または `ApiKey:Key`）
  - レース・出馬表・結果、予想票・印、馬・騎手・調教師・馬主（登録／編集／別名統合）、メモ、ML 予測（`/api/races/{raceId}/ml-prediction`）・ML 学習（`/api/ml/train`）など。全エンドポイントは `src/HorseRacingPrediction.Api/EndpointExtensions.cs` を参照
- 管理画面: Blazor Server（ルート直下、Cookie 認証。ログイン画面 `/login` はユーザー名 `user` 固定、パスワードは `ApiKey:Key` と同じ値）
  - 既存の JSON API を自己ループバック HTTP で呼び出すのみで、コマンド/クエリを直接実行しない
- OpenAPI: Swagger UI + OpenAPI JSON を自動生成

### 実行方法

1. API キーを設定

```bash
export HORSE_RACING_API_KEY="your-local-api-key"
```

2. ビルド

```bash
dotnet build HorseRacingPrediction.sln
```

3. Api を起動

```bash
dotnet run --project src/HorseRacingPrediction.Api/HorseRacingPrediction.Api.csproj
```

- Swagger UI: `/swagger`
- OpenAPI JSON: `/swagger/v1/swagger.json`
- 管理画面: `/races` など（起動後にログイン画面へリダイレクト）

4. Collector / Predictor を起動する場合（Api の URL・API キーを設定してから実行）

```bash
dotnet run --project src/HorseRacingPrediction.Collector/HorseRacingPrediction.Collector.csproj
dotnet run --project src/HorseRacingPrediction.Predictor/HorseRacingPrediction.Predictor.csproj
```

Collector は現在、実際の収集実行経路が一時的に無効化されています（上記「サービス構成」参照）。

5. テストを実行

```bash
dotnet test HorseRacingPrediction.sln
```

### SQLite スキーマ変更

DBモデルを変更した場合は、ローカルツールを復元してMigrationを追加します。

```bash
dotnet tool restore
dotnet ef migrations add <MigrationName> \
  --project src/HorseRacingPrediction.Infrastructure/HorseRacingPrediction.Infrastructure.csproj \
  --context EventStoreDbContext \
  --output-dir Persistence/Migrations
dotnet ef migrations has-pending-model-changes \
  --project src/HorseRacingPrediction.Infrastructure/HorseRacingPrediction.Infrastructure.csproj
dotnet test HorseRacingPrediction.sln
```

生成されたMigration、ModelSnapshot、対応テストを同じ変更に含めてください。
API起動時に未適用Migrationが自動適用されます。既存DBは初回のみスキーマを照合して
`InitialEventStore` を適用済みとして登録します。照合に失敗した場合はDBを変更せず起動を停止します。

### EventFlow 実装メモ

- EventFlow v1 系では `EventFlow.AspNetCore` ではなく `EventFlow` パッケージを優先
- DI 登録は `AddEventFlow(...).AddDefaults(Assembly)` を基準に構成
- 集約は `AggregateRoot` + `IEmit<TEvent>`、コマンドは `Command` + `CommandHandler` で実装
