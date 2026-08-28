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
  HorseRacingPrediction.Domain/         # 集約・イベント・状態・値オブジェクト（EventFlow）
  HorseRacingPrediction.Application/    # コマンド・ハンドラー・ReadModel 定義
  HorseRacingPrediction.Infrastructure/ # EF永続化・DbContext・サービス拡張
  HorseRacingPrediction.Api/            # Minimal API エンドポイント・認証・コントラクト
tests/
  HorseRacingPrediction.Domain.Tests/       # 集約の単体テスト
  HorseRacingPrediction.Application.Tests/  # コマンド発行→状態検証テスト
  HorseRacingPrediction.Infrastructure.Tests/ # EF永続化テスト
  HorseRacingPrediction.Api.Tests/          # APIエンドポイント統合テスト
```

- **Domain層**: EventFlow の `AggregateRoot`、`IAggregateEvent`、状態管理、値オブジェクト、列挙型。外部依存なし（EventFlow のみ）。
- **Application層**: `Commands/{AggregateRoot}/` にコマンド・ハンドラー、`Queries/ReadModels/` に ReadModel 定義。Domain への参照を持つ。
- **Infrastructure層**: `IDbContextProvider<EventStoreDbContext>` と `SqliteDbContextProvider` で SQLite インメモリ／ファイルベースの Event Store を提供。
- **Api層**: エンドポイント定義は `EndpointExtensions.cs` に集約。`Program.cs` は DI 設定と `app.MapApiEndpoints()` のみ。
- **EventFlow 登録**: `AddDefaults` は Domain アセンブリと Application アセンブリの両方をスキャンする。

設計ドキュメント: [docs/domain-design.md](../docs/domain-design.md), [docs/automation-design.md](../docs/automation-design.md), [docs/system-architecture.md](../docs/system-architecture.md), [docs/collector-design.md](../docs/collector-design.md), [docs/predictor-design.md](../docs/predictor-design.md)

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

## Git Workflow

- 変更は、1つの目的として説明できるまとまりが完成し、関連するビルド・テストが成功した時点でコミットする
- 長時間の作業では、独立して検証可能な区切りごとにコミットし、未検証または途中状態の変更をまとめて残さない
- コミット前に `git diff --check` と `git status` を確認し、生成物、秘密情報、目的外の変更を含めない
- ユーザーが作成した既存変更は、内容を確認せずに上書き、取り消し、または別目的のコミットへ混在させない
- コミットメッセージは、変更の目的が分かる簡潔な命令形にする

## AWS Deployment Policy

- AWS リソース、Terraform resource、または GitHub Actions の AWS API 操作を追加・変更・削除する場合は、同じ変更内で `docs/lightsail-deployment.md` の GitHub Actions IAM ポリシーを見直す
- IAM ポリシーには実際の workflow と Terraform が必要とする操作だけを含め、リソースレベル制限が可能な操作は本システムの ARN に限定する
- IAM ポリシーを変更した場合は、初回作成手順だけでなく既存ポリシーの更新手順でも適用できることを確認する

## Conventions

### File Organization

- 原則として 1ファイル1クラスを守る
- `class`, `record`, `enum`, `interface` は、ネストした補助型や言語仕様上やむを得ない例外を除き、1型ごとに独立ファイルへ配置する
- 一時的な実装都合で複数型を同居させず、追加時点で対応ファイルを分割する

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
