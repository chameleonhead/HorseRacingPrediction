# Project Guidelines

## Overview

競馬予想アプリケーション — CQRS + Event Sourcing アーキテクチャ（EventFlow）を採用した ASP.NET Core Web API。

## Tech Stack

- .NET 8.0 / ASP.NET Core Minimal APIs
- EventFlow 1.2.3 (CQRS + Event Sourcing)
- EventFlow.EntityFramework 1.2.3 + SQLite (永続化)
- Swashbuckle 6.5.0 (Swagger/OpenAPI)
- MSTest 3.1.1 (テストフレームワーク)

## Architecture

```
src/
  HorseRacingPrediction.Domain/       # 集約・イベント・コマンド（EventFlow）
  HorseRacingPrediction.Infrastructure/ # EF永続化・DbContext・サービス拡張
  HorseRacingPrediction.Api/          # Minimal API エンドポイント・認証・コントラクト
tests/
  HorseRacingPrediction.Domain.Tests/       # 集約の単体テスト
  HorseRacingPrediction.Application.Tests/  # コマンド発行→状態検証テスト
  HorseRacingPrediction.Infrastructure.Tests/ # EF永続化テスト
  HorseRacingPrediction.Api.Tests/          # APIエンドポイント統合テスト
```

- **Domain層**: EventFlow の `AggregateRoot`、`IAggregateEvent`、`Command` / `CommandHandler` で構成。外部依存なし。
- **Infrastructure層**: `IDbContextProvider<EventStoreDbContext>` と `SqliteDbContextProvider` で SQLite インメモリ／ファイルベースの Event Store を提供。
- **Api層**: エンドポイント定義は `EndpointExtensions.cs` に集約。`Program.cs` は DI 設定と `app.MapApiEndpoints()` のみ。

設計ドキュメント: [docs/domain-design.md](docs/domain-design.md), [docs/automation-design.md](docs/automation-design.md)

## Build and Test

```bash
# ビルド
dotnet build HorseRacingPrediction.sln

# 全テスト実行
dotnet test HorseRacingPrediction.sln

# 特定プロジェクトのテスト
dotnet test tests/HorseRacingPrediction.Domain.Tests
dotnet test tests/HorseRacingPrediction.Api.Tests
```

## Conventions

### Domain

- 集約IDは `EventFlow.Core.Identity<T>` を継承した専用型を使用（`RaceId`, `PredictionTicketId`）
- 集約の状態は `AggregateState<TAggregate, TIdentity, TState>` で管理
- 状態遷移の不正はドメイン層で `InvalidOperationException` をスロー

### Testing

- テストフレームワークは **MSTest** を使用する
  - `[TestClass]`, `[TestMethod]`, `[TestInitialize]`, `[TestCleanup]`
  - `Assert.AreEqual`, `Assert.IsTrue`, `Assert.IsNotNull`, `Assert.ThrowsException<T>`
- API テストは `WebApplicationFactory<Program>` を **使わない**
  - `WebApplication.CreateBuilder()` + `UseTestServer()` + テスト専用 DI で構成（`TestApplicationFactory.cs`）
  - `public partial class Program` パターンは禁止
- テストプロジェクトは対応するレイヤーごとに分離

### API

- エンドポイントは `EndpointExtensions.MapApiEndpoints()` に集約し、`Program.cs` と `TestApplicationFactory` で共有
- POST エンドポイントには `ApiKeyEndpointFilter` による API キー認証を適用
- Swagger アノテーション（`SwaggerOperation`）を付与

### EventFlow EntityFramework

- `UseEntityFrameworkEventStore` の前に `ConfigureEntityFramework(EntityFrameworkConfiguration.New)` を登録すること
- テスト時は `SqliteDbContextProvider` を直接 DI に登録し、`IDbContextProvider<EventStoreDbContext>` としても登録する
