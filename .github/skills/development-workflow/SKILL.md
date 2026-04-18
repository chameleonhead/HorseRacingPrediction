---
name: development-workflow
description: "開発ワークフロースキル。Use when: 新機能追加、バグ修正、リファクタリング、テスト作成、集約追加、エンドポイント追加、EventFlow CQRS+ES 実装。dotnet build, dotnet test, MSTest, Minimal API, EventFlow, CQRS, Event Sourcing の開発手順。"
---

# 開発ワークフロー

HorseRacingPrediction プロジェクトの開発手順。変更を加える際は、このワークフローに従う。

## When to Use

- 新しい集約・イベント・コマンドを追加するとき
- API エンドポイントを追加・変更するとき
- バグ修正やリファクタリングを行うとき
- テストを作成・修正するとき

## 開発フロー

### 1. 影響範囲の確認

変更対象のレイヤーを特定する。

| 変更内容 | 対象レイヤー | テストプロジェクト |
|---------|------------|----------------|
| ビジネスルール、状態遷移 | Domain | Domain.Tests |
| コマンド・ハンドラー | Application | Application.Tests |
| ReadModel 定義 | Application | Application.Tests |
| DB永続化、DbContext | Infrastructure | Infrastructure.Tests |
| HTTPエンドポイント、認証 | Api | Api.Tests |

### 2. 実装

#### Domain 層の変更

```
src/HorseRacingPrediction.Domain/
├── {AggregateRoot}/
│   ├── {Name}Aggregate.cs      # 集約ルート
│   ├── {Name}State.cs          # 状態管理
│   ├── {Name}Events.cs         # イベント定義
│   ├── {Name}Id.cs             # Identity 型
│   └── {Name}Status.cs         # 状態列挙（必要な場合）
```

- `EventFlow.Aggregates.AggregateRoot<TAggregate, TIdentity>` を継承
- 状態の不正遷移は `InvalidOperationException` をスロー
- イベントは不変な record を推奨

#### Application 層の変更

```
src/HorseRacingPrediction.Application/
├── Commands/
│   └── {AggregateRoot}/
│       └── {Name}Commands.cs       # コマンド＋ハンドラー
└── Queries/
    └── ReadModels/
        ├── RacePredictionContextReadModel.cs
        ├── RaceResultViewReadModel.cs
        └── PredictionComparisonViewReadModel.cs
```

- コマンドとコマンドハンドラーは `Commands/{AggregateRoot}/` に配置
- ReadModel 定義は `Queries/ReadModels/` に配置

#### Infrastructure 層の変更

- `EventStoreDbContext` は EventFlow の `AddEventFlowEvents()` / `AddEventFlowSnapshots()` を使用
- `UseEntityFrameworkEventStore` の前に必ず `ConfigureEntityFramework(EntityFrameworkConfiguration.New)` を登録

#### Api 層の変更

- **エンドポイント追加**: [EndpointExtensions.cs](../../../src/HorseRacingPrediction.Api/EndpointExtensions.cs) の `MapApiEndpoints()` メソッド内に追加
- **Program.cs は変更しない**（DI 設定の変更時のみ）
- POST エンドポイントには `.AddEndpointFilter<ApiKeyEndpointFilter>()` を適用
- `SwaggerOperation` アノテーションを付与

### 3. テスト作成

#### ルール

- フレームワークは **MSTest** を使用
- `[TestClass]`, `[TestMethod]`, `[TestInitialize]`, `[TestCleanup]` を使用
- xUnit のアトリビュート（`[Fact]`, `[Theory]`）は使わない
- `Assert.AreEqual`, `Assert.IsTrue`, `Assert.IsNotNull`, `Assert.ThrowsException<T>` を使用

#### Domain テスト

集約を直接インスタンス化してテスト。DI 不要。

```csharp
[TestClass]
public class SampleAggregateTests
{
    [TestMethod]
    public void Create_SetsDetailsCorrectly()
    {
        var aggregate = new SampleAggregate(SampleId.New);
        aggregate.Create(/* args */);
        var details = aggregate.GetDetails();
        Assert.AreEqual(expected, details.Property);
    }
}
```

#### Application テスト

EventFlow の DI でコマンドバスを構築してテスト。

```csharp
[TestClass]
public class SampleCommandTests
{
    private ServiceProvider _serviceProvider = null!;
    private ICommandBus _commandBus = null!;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEventFlow(options =>
        {
            options.AddDefaults(typeof(RaceAggregate).Assembly);
            options.AddDefaults(typeof(CreateRaceCommand).Assembly);
        });
        _serviceProvider = services.BuildServiceProvider();
        _commandBus = _serviceProvider.GetRequiredService<ICommandBus>();
    }

    [TestCleanup]
    public void Cleanup() => _serviceProvider.Dispose();
}
```

#### Infrastructure テスト

EF 永続化テストでは `SqliteDbContextProvider` を直接登録。

```csharp
[TestInitialize]
public void Setup()
{
    var services = new ServiceCollection();
    services.AddLogging();
    var dbContextProvider = new SqliteDbContextProvider("DataSource=:memory:");
    services.AddSingleton(dbContextProvider);
    services.AddSingleton<IDbContextProvider<EventStoreDbContext>>(dbContextProvider);
    services.AddEventFlow(options =>
    {
        options
            .ConfigureEntityFramework(EntityFrameworkConfiguration.New)
            .AddDefaults(typeof(RaceAggregate).Assembly)
            .UseEntityFrameworkEventStore<EventStoreDbContext>();
    });
    _serviceProvider = services.BuildServiceProvider();
}
```

#### Api テスト

`TestApplicationFactory` を使用。`WebApplicationFactory<Program>` は使わない。

```csharp
[TestClass]
public class SampleEndpointsTests
{
    private static WebApplication _app = null!;
    private static HttpClient _client = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext context)
    {
        (_app, _client) = await TestApplicationFactory.CreateAsync();
        _client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
    }

    [ClassCleanup]
    public static async Task ClassClean()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }
}
```

### 4. 検証

変更後は必ずビルドとテストを実行する。

```bash
# ビルド確認
dotnet build HorseRacingPrediction.sln

# 全テスト実行
dotnet test HorseRacingPrediction.sln

# 変更したレイヤーのテストのみ実行（高速フィードバック）
dotnet test tests/HorseRacingPrediction.{Layer}.Tests
```

- ビルドエラー 0、テスト全成功を確認してから完了とする
- 失敗がある場合は原因を特定して修正し、再度テストを実行する

## チェックリスト

- [ ] 影響レイヤーを特定した
- [ ] 実装を完了した
- [ ] 対応するテストを作成・更新した
- [ ] `dotnet build HorseRacingPrediction.sln` がエラー 0 で成功する
- [ ] `dotnet test HorseRacingPrediction.sln` が全テスト成功する
