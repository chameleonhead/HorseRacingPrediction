# HorseRacingPrediction

CQRS+ES と ASP.NET Core Web API を前提にした競馬予想アプリケーションの設計・実装用リポジトリです。

## 設計ドキュメント

- [docs/domain-design.md](docs/domain-design.md): CQRS+ES 前提の競馬予想ドメイン設計
- [docs/automation-design.md](docs/automation-design.md): 自動処理の責務設計

## 現時点の方針

- 収集対象はレース、馬、騎手、調教師、およびそれらに紐づく一次テキスト情報
- 正規化済みの構造化データと、原文・元データを両方保持する
- レース前の予想とレース後の結果を同一ライフサイクル上で管理する
- 書き込みモデルは CQRS + Event Sourcing を前提に設計する
- ASP.NET Core Web API と API キー認証を前提にする
- 予想は同一レースに複数登録できるようにする

## 実装状況（スタンドアロン Web API）

`src/HorseRacingPrediction.Api` に、EventFlow を利用したスタンドアロン API を実装済みです。

- アーキテクチャ: CQRS + Event Sourcing（EventFlow 1.2.3）
- エンドポイント実装: ASP.NET Core Minimal API
- 認証: `X-Api-Key` ヘッダー（`HORSE_RACING_API_KEY` または `ApiKey:Key`）
- OpenAPI: Swagger UI + OpenAPI JSON を自動生成

### 主要エンドポイント

- `POST /api/races/{raceId}`: レース作成
- `POST /api/races/{raceId}/card/publish`: 出馬表公開
- `POST /api/races/{raceId}/result`: 結果確定
- `GET /api/races/{raceId}`: レース取得
- `POST /api/predictions/{predictionTicketId}`: 予想チケット作成
- `POST /api/predictions/{predictionTicketId}/marks`: 印追加
- `GET /api/predictions/{predictionTicketId}`: 予想チケット取得

### 実行方法

1. API キーを設定

```bash
export HORSE_RACING_API_KEY="your-local-api-key"
```

2. ビルド

```bash
dotnet build HorseRacingPrediction.sln
```

3. 起動

```bash
dotnet run --project src/HorseRacingPrediction.Api/HorseRacingPrediction.Api.csproj
```

4. OpenAPI/Swagger

- Swagger UI: `/swagger`
- OpenAPI JSON: `/swagger/v1/swagger.json`

### EventFlow 実装メモ

- EventFlow v1 系では `EventFlow.AspNetCore` ではなく `EventFlow` パッケージを優先
- DI 登録は `AddEventFlow(...).AddDefaults(Assembly)` を基準に構成
- 集約は `AggregateRoot` + `IEmit<TEvent>`、コマンドは `Command` + `CommandHandler` で実装
