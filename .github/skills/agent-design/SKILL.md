---
name: agent-design
description: "エージェント設計スキル。Use when: エージェント新規作成、ツール設計、システムプロンプト作成、ワークフロー構築、WebBrowserAgent 改修、PlaywrightTools 改修、エージェントのデバッグ。Agent design, tool design, system prompt, ACI, workflow patterns, Microsoft.Extensions.AI, ChatClientAgent。"
---

# エージェント設計ガイド

LLM エージェントおよびツール（Plugin）の設計・実装・改善を行うための手順とベストプラクティス。

## When to Use

- 新しいエージェントを作成するとき
- 新しいツール（Plugin）を作成するとき
- 既存エージェントのツールを追加・変更するとき
- システムプロンプトを設計・改善するとき
- エージェントのワークフロー（Sequential / Parallel / Orchestrator-workers）を構築するとき
- エージェントが期待どおりに動かないときのデバッグ

## 参照資料

- [Building effective agents](https://www.anthropic.com/engineering/building-effective-agents)（Anthropic, 2024-12-19）
- [Equipping agents for the real world with Agent Skills](https://www.anthropic.com/engineering/equipping-agents-for-the-real-world-with-agent-skills)（Anthropic, 2025-10-16）
- [Agent Skills specification](https://agentskills.io/specification)
- [VS Code Agent Skills](https://code.visualstudio.com/docs/copilot/customization/agent-skills)
- [.NET AI Agents](https://learn.microsoft.com/en-us/dotnet/ai/conceptual/agents)

---

## 1. 設計原則

### 1.1 シンプルさを維持する

> 最も成功している実装は、複雑なフレームワークではなく、シンプルで組み合わせ可能なパターンを使っている。
> — Anthropic "Building effective agents"

- **まず単一の LLM 呼び出し + ツール** で解決できないか検討する
- 複雑さは「計測して改善が確認できたとき」だけ追加する
- エージェント = **LLM + ツール + ループ**（環境からのフィードバックに基づく）

### 1.2 ツール設計はプロンプト以上に重要（ACI = Agent-Computer Interface）

> We actually spent more time optimizing our tools than the overall prompt.
> — Anthropic（SWE-bench エージェント開発の知見）

- **ツールの説明文（Description）** にプロンプトエンジニアリングと同等の工数をかける
- ツール名・パラメータ名は、モデルにとって意図が明白であること
- 使い分けが曖昧なツールは統合するか、説明で明確に差別化する

### 1.3 ポカヨケ（Poka-yoke）

ツールの引数設計で、モデルがミスしにくい仕組みにする。

| 問題 | 対策例 |
|------|--------|
| 相対パスで混乱 | 常に絶対パスを要求する |
| 検索後にページ読み忘れ | 検索 + 読み込みを 1 ツールに統合する（`BrowserSearchAndRead`） |
| URL を推測で生成 | ツール結果の URL だけ使うようプロンプトで制約 |
| ツール選択ミス | テーブル形式で選択指針をプロンプトに記載 |

### 1.4 環境からの Ground Truth

- 各ステップでツール結果（実データ）を取得し、それを根拠に次の行動を決定する
- ページタイトルだけで要約しない。必ず本文を読んでから回答する

---

## 2. ワークフローパターン

用途に応じて適切なパターンを選択する。

| パターン | 適用場面 | 実装難易度 |
|---------|---------|-----------|
| **Prompt chaining** | 固定ステップに分解可能なタスク | 低 |
| **Routing** | 入力種別で処理を分岐させる | 低 |
| **Parallelization** | 独立サブタスクの並行実行 | 中 |
| **Orchestrator-workers** | サブタスクが動的に決まる | 中 |
| **Evaluator-optimizer** | 反復改善で品質が上がる | 中 |
| **Autonomous agent** | ステップ数が予測不能な探索 | 高 |

### このプロジェクトでの使い分け

```
WebBrowserAgent          → Autonomous agent（ブラウザツールを自律利用）
DataCollectionWorkflow   → Parallelization（4 エージェント並行）
PredictionWorkflow       → Prompt chaining（3 ステップ順次）
WeeklyScheduleWorkflow   → Orchestrator-workers（動的にレース発見 → 収集 → 予測）
```

---

## 3. システムプロンプト設計

### 3.1 テンプレート構造

```markdown
## 役割（1〜2 文）
## ツール選択の指針（テーブル形式推奨）
## 行動手順（番号付きステップ、5 個以下）
## ルール（必須制約、3〜5 個以下）
```

### 3.2 実例: WebBrowserAgent のシステムプロンプト

```csharp
public const string SystemPrompt = """
    あなたは Web 調査を行うブラウザエージェントです。
    ブラウザツールを使って情報を収集し、日本語の Markdown で返します。

    ## ツール選択の指針
    | 状況 | 使うツール |
    |------|-----------|
    | 情報を探したい（最優先） | BrowserSearchAndRead |
    | 既知の URL を読みたい | BrowserNavigate |
    | ページのリンク一覧が必要 | BrowserGetLinks |
    | 検索結果リンクだけ必要 | BrowserSearch |

    ## 行動手順
    1. 依頼から「検索キーワード」と「対象サイトのドメイン」を特定する
    2. 対象サイトが分かれば site パラメータに指定する
    3. まず BrowserSearchAndRead で検索し本文を読む
    4. 情報が不足なら BrowserGetLinks でリンクを確認し BrowserNavigate で追加ページを読む
    5. 十分な情報が揃ったら Markdown で整理して返す

    ## ルール
    - URL を推測・生成しない。ツールが返した URL だけ使う
    - ページ本文を読んでから回答する
    - 参照 URL を明記する
    """;
```

**ポイント**:
- 役割を 2 文で簡潔に定義
- **テーブル形式** でツール選択を示す（小規模モデルが解釈しやすい）
- 最優先ツールを明示（「まず BrowserSearchAndRead で検索し本文を読む」）
- ルールは **3 個**。短く具体的

### 3.3 実例: 構造化出力を求める場合（WeekendRaceDiscoveryAgent）

```csharp
public const string SystemPrompt = """
    あなたは週末の競馬レーススケジュールを調査する専門エージェントです。
    ...
    **必ず** 以下の JSON 配列形式のみで返してください。
    説明文・Markdown・コードブロック以外のテキストは一切含めないこと。

    ## 出力形式（JSON 配列）
    ```json
    [
      {
        "raceName": "レース名",
        "raceDate": "YYYY-MM-DD",
        ...
      }
    ]
    ```
    ...
    """;
```

**ポイント**: JSON 出力が必要な場合はフォーマットを具体例で示す

### 3.4 アンチパターン

- ツール一覧を羅列するだけ（→ テーブルで使い分けを示す）
- 10 個以上のルール（→ 5 個以下に絞る。多すぎると全部無視される）
- 矛盾するルール（→ 最優先を 1 つに決める）
- 「〇〇してはいけない」だけ（→ 代わりに何をすべきか書く）

---

## 4. エージェントの作り方

このプロジェクトではすべてのエージェントが同じ基本構造を共有する。

### 4.1 基本構造（ChatClientAgent ラッパーパターン）

```
src/HorseRacingPrediction.Agents/Agents/
├── {AgentName}.cs           # 1 エージェント = 1 ファイル
```

すべてのエージェントは以下の構造に従う:

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Agents;

public sealed class MyAgent
{
    public const string AgentName = "MyAgent";

    public const string SystemPrompt = """
        ...（§3 のテンプレートに従う）
        """;

    private readonly ChatClientAgent _innerAgent;

    // コンストラクタ: IChatClient + IList<AITool>
    public MyAgent(IChatClient chatClient, IList<AITool> tools)
    {
        _innerAgent = new ChatClientAgent(
            chatClient,
            name: AgentName,
            instructions: SystemPrompt,
            tools: tools);
    }

    // 公開 API（パターンは §4.2 を参照）
    public async Task<string> InvokeAsync(
        string userMessage, CancellationToken ct = default)
    {
        var result = await _innerAgent.RunAsync(userMessage, cancellationToken: ct);
        return result.Text;
    }
}
```

**必須要素**:
- `sealed class`
- `const string AgentName` — DI やログで使用
- `const string SystemPrompt` — §3 テンプレートに従う
- `private readonly ChatClientAgent _innerAgent` — 内部エージェント
- コンストラクタは `IChatClient` + `IList<AITool>` を受け取る

### 4.2 エージェントの 3 つのパターン

#### パターン A: テキスト出力エージェント（最も一般的）

Markdown テキストを返す。`HorseDataAgent`, `JockeyDataAgent`, `RaceDataAgent` など。

```csharp
public async Task<string> CollectAsync(
    string horseName, CancellationToken ct = default)
{
    var result = await _innerAgent.RunAsync(
        $"競走馬「{horseName}」の詳細情報を収集してください。",
        cancellationToken: ct);
    return result.Text;
}
```

**ポイント**: 公開メソッド名はタスクを表す動詞にする（`CollectAsync`, `AnalyzeAsync`, `PredictAsync`）

#### パターン B: Agent-as-Tool パターン（他エージェントのツールになる）

`WebBrowserAgent` がこのパターンを使用。自分自身を `AIFunction` として公開する。

```csharp
public sealed class WebBrowserAgent
{
    // ... 基本構造 ...

    // 直接呼び出し用
    public async Task<string> InvokeAsync(
        string userMessage, CancellationToken ct = default)
    {
        var result = await _innerAgent.RunAsync(userMessage, cancellationToken: ct);
        return result.Text;
    }

    // 他エージェントのツールとして公開
    public AIFunction CreateAIFunction() => _innerAgent.AsAIFunction();

    // DI ファクトリ
    public static WebBrowserAgent CreateFromServices(IServiceProvider services)
    {
        var chatClient = services.GetRequiredService<IChatClient>();
        var browser = services.GetRequiredService<IWebBrowser>();
        var options = services.GetRequiredService<IOptions<WebFetchOptions>>();
        var playwrightTools = new PlaywrightTools(browser, options);
        return new WebBrowserAgent(chatClient, playwrightTools.GetAITools());
    }
}
```

**利用側**: `webBrowserAgent.CreateAIFunction()` を `tools` リストに追加する

```csharp
var tools = new List<AITool>(horseRacingTools.GetAITools())
{
    webBrowserAgent.CreateAIFunction()  // エージェントをツールとして追加
};
return new HorseDataAgent(chatClient, tools);
```

#### パターン C: 構造化出力エージェント（JSON → DTO に変換）

`WeekendRaceDiscoveryAgent` がこのパターンを使用。エージェントの応答 JSON をパースして型付きオブジェクトを返す。

```csharp
public async Task<IReadOnlyList<WeekendRaceInfo>> DiscoverAsync(
    DateOnly targetWeekend, CancellationToken ct = default)
{
    var prompt = $"...{saturday:yyyy年M月d日}...のレースを JSON 配列で返してください。";
    var result = await _innerAgent.RunAsync(prompt, cancellationToken: ct);
    return ParseRaceInfoList(result.Text);  // JSON → DTO 変換
}

private static IReadOnlyList<WeekendRaceInfo> ParseRaceInfoList(string text)
{
    var jsonText = ExtractJsonArray(text);       // ```json...``` ブロック抽出
    if (string.IsNullOrWhiteSpace(jsonText)) return [];
    try
    {
        var dtos = JsonSerializer.Deserialize<List<WeekendRaceInfoDto>>(
            jsonText,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return dtos?.ConvertAll(ToWeekendRaceInfo) ?? [];
    }
    catch (JsonException) { return []; }
}
```

**ポイント**:
- システムプロンプトに JSON フォーマット例を明示する
- `ExtractJsonArray` で ` ```json ``` ` ブロックと素の `[...]` の両方に対応
- `JsonException` を catch して空リストを返す（LLM 出力は不安定なため）
- DTO（内部型）→ public record への変換メソッドを用意する

### 4.3 エージェントの配置

```
src/HorseRacingPrediction.Agents/
├── Agents/              # エージェントクラス（1 ファイル 1 クラス）
│   ├── WebBrowserAgent.cs
│   ├── HorseDataAgent.cs
│   └── ...
├── Plugins/             # ツール（Plugin）クラス
├── Browser/             # ブラウザ抽象化（IWebBrowser, PlaywrightWebBrowser）
├── ChatClients/         # LLM クライアント（LMStudioChatClient）
└── Workflow/            # ワークフロー・DI 登録
```

---

## 5. ツール（Plugin）の作り方

### 5.1 基本構造

```
src/HorseRacingPrediction.Agents/Plugins/
├── {ToolName}Tools.cs           # 1 プラグイン = 1 ファイル
```

すべてのツールプラグインは以下の構造に従う:

```csharp
using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Plugins;

public sealed class MyTools
{
    // DI で受け取る依存（必要に応じて）
    private readonly ISomeDependency _dependency;

    public MyTools(ISomeDependency dependency)
    {
        _dependency = dependency;
    }

    // ツールメソッド: [Description] 属性が AI ツール定義を生成する
    [Description("何をするか + いつ使うか + 他ツールとの違い")]
    public async Task<string> DoSomething(
        [Description("パラメータの説明（制約や形式を含む）")] string param,
        CancellationToken cancellationToken = default)
    {
        // 実装
        return "結果テキスト";
    }

    // AITool 一覧を返すメソッド（必須）
    public IList<AITool> GetAITools() =>
    [
        AIFunctionFactory.Create(DoSomething),
    ];
}
```

**必須要素**:
- `sealed class`
- 各メソッドに `[Description("...")]` 属性（ツール説明文）
- 各パラメータに `[Description("...")]` 属性（パラメータ説明文）
- `GetAITools()` メソッドで `AIFunctionFactory.Create()` を使って一覧を返す
- 戻り値は **`string`**（LLM が読むテキスト。Markdown テーブル推奨）

### 5.2 ツールの 4 カテゴリ

#### カテゴリ 1: ブラウザプリミティブ（PlaywrightTools）

`IWebBrowser` を直接操作する低レベルツール。`WebBrowserAgent` 専用。

```csharp
public sealed class PlaywrightTools
{
    private readonly IWebBrowser _browser;
    private readonly WebFetchOptions _options;

    public PlaywrightTools(IWebBrowser browser, IOptions<WebFetchOptions> options)
    {
        _browser = browser;
        _options = options.Value;
    }

    [Description("検索して上位ページの本文を一括取得する最優先ツールです。関連サブページも自動的に読みます。")]
    public async Task<string> BrowserSearchAndRead(
        [Description("検索キーワード（スペース区切り）")] string query,
        [Description("検索対象サイトのドメイン（例: www.jra.go.jp）省略可")] string? site = null,
        [Description("読み込むページ数（既定値 3）")] int maxPages = 3,
        CancellationToken cancellationToken = default)
    {
        // 検索 → ページ取得 → サブリンク自動追跡 を 1 呼び出しで実行
        ...
    }

    [Description("指定 URL のページ本文テキストを取得します。")]
    public async Task<string> BrowserNavigate(...) { ... }

    [Description("ページ内のリンク一覧を抽出します。")]
    public async Task<string> BrowserGetLinks(...) { ... }

    [Description("検索エンジンでクエリを実行し、リンク一覧だけを返します。本文は読みません。")]
    public async Task<string> BrowserSearch(...) { ... }

    public IList<AITool> GetAITools() =>
    [
        AIFunctionFactory.Create(BrowserNavigate),
        AIFunctionFactory.Create(BrowserGetLinks),
        AIFunctionFactory.Create(BrowserSearch),
        AIFunctionFactory.Create(BrowserSearchAndRead),
    ];
}
```

**ポイント**:
- `ValidateDomain(url)` で許可ドメインを検証する（セキュリティ）
- 複合ツール（`BrowserSearchAndRead`）で小規模モデルのステップ忘れを防ぐ
- サブリンク自動追跡で「概要→詳細」パターンに対応

#### カテゴリ 2: エージェント委譲ツール（WebFetchTools）

`WebBrowserAgent` にメッセージを送り、エージェントの応答をそのまま返す。

```csharp
public sealed class WebFetchTools
{
    private readonly Func<string, CancellationToken, Task<string>> _invokeAgent;

    // プロダクション: WebBrowserAgent を使用
    public WebFetchTools(WebBrowserAgent agent)
    {
        _invokeAgent = agent.InvokeAsync;
    }

    // テスト: デリゲートで差し替え可能
    internal WebFetchTools(Func<string, CancellationToken, Task<string>> invokeAgent)
    {
        _invokeAgent = invokeAgent;
    }

    [Description("自然言語で Web 検索を行い、上位サイトを順に調査しながら必要な情報が得られるまで探索します。")]
    public async Task<string> SearchWeb(
        [Description("検索クエリ文字列。自然言語で指定可能です")] string query,
        [Description("知りたい内容や調査目的")] string? objective = null,
        [Description("検索対象を絞り込むドメイン（省略可）")] string? site = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Web 検索を行い、情報を収集して日本語の Markdown で返してください。");
        sb.AppendLine($"検索クエリ: {query}");
        if (!string.IsNullOrWhiteSpace(objective))
            sb.AppendLine($"調査目的: {objective}");
        if (!string.IsNullOrWhiteSpace(site))
            sb.AppendLine($"対象サイト: {site}");
        return await _invokeAgent(sb.ToString(), cancellationToken);
    }

    public IList<AITool> GetAITools() => [ ... ];
}
```

**ポイント**:
- `Func<string, CancellationToken, Task<string>>` でテスト可能にする
- 内部コンストラクタ（`internal`）でテスト用差し替えを提供

#### カテゴリ 3: ドメイン固有ツール（HorseRacingTools）

`WebFetchTools` をラップし、特定ドメインの URL 構築やクエリ最適化を行う。

```csharp
public sealed class HorseRacingTools
{
    private readonly WebFetchTools _webFetchTools;

    public HorseRacingTools(WebFetchTools webFetchTools)
    {
        _webFetchTools = webFetchTools;
    }

    [Description("JRA の出馬表ページを取得します。競馬場コード・日付・レース番号から URL を構築します。")]
    public async Task<string> FetchRaceCard(
        [Description("競馬場コード（例: 05=東京）")] string racecourseCode,
        [Description("開催日（yyyyMMdd 形式）")] string raceDate,
        [Description("レース番号（1〜12）")] int raceNumber,
        CancellationToken cancellationToken = default)
    {
        var url = $"https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01sde0203_{raceDate}{racecourseCode}{raceNumber:D2}01&sub=";
        return await _webFetchTools.SearchAndFetchContentAsync(
            $"JRA 出馬表 {raceDate}", site: "www.jra.go.jp", cancellationToken: cancellationToken);
    }

    public IList<AITool> GetAITools() => [ ... ];
}
```

**ポイント**:
- URL 構築ロジックをモデルに任せず、ツール側で組み立てる（ポカヨケ）
- ドメイン層でのみドメイン固有キーワード（netkeiba, JRA 等）を使用する

#### カテゴリ 4: ユーティリティツール（CalendarTools）

外部依存なし（または `TimeProvider` 等の軽量依存）のシンプルなツール。

```csharp
public sealed class CalendarTools
{
    private readonly TimeProvider _timeProvider;

    public CalendarTools(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    [Description("現在の日本時間（JST）の日時を取得します。年月日・曜日・時分を返します。")]
    public string GetCurrentDateTime()
    {
        var jst = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        var now = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), jst);
        return $"{now:yyyy年M月d日}（{now.ToString("dddd", new CultureInfo("ja-JP"))}）{now:H時m分}";
    }

    [Description("指定日から最も近い次の週末（土・日）の日付を返します。")]
    public string GetWeekendDates(
        [Description("基準日（yyyy-MM-dd 形式、省略時は今日）")] string? baseDate = null) { ... }

    public IList<AITool> GetAITools() =>
    [
        AIFunctionFactory.Create(GetCurrentDateTime),
        AIFunctionFactory.Create(GetWeekendDates),
        AIFunctionFactory.Create(GetJraRacecourseCode)
    ];
}
```

**ポイント**:
- `TimeProvider` で DI / テスト対応（`TimeProvider.System` をデフォルトに）
- 同期メソッドも `AIFunctionFactory.Create()` で登録可能

### 5.3 ツール説明文（Description）の書き方

```csharp
// 悪い例: 何をするかだけ
[Description("検索します")]

// 良い例: 何をするか + いつ使うか + 他ツールとの違い
[Description("検索して上位ページの本文を一括取得する最優先ツールです。関連サブページも自動的に読みます。情報を探すときはまずこれを使ってください。")]
```

- **何をするか** + **いつ使うか** + **他ツールとの違い** を含める
- ジュニア開発者向けの docstring を書くつもりで記述する
- 最優先ツールには「まずこれを使ってください」と明記する

### 5.4 パラメータ説明の書き方

```csharp
// 悪い例: 型名だけ
[Description("URL")] string url

// 良い例: 制約・形式・取得元を明示
[Description("移動先の URL（ツールが返した URL を指定すること）")] string url
[Description("開催日（yyyyMMdd 形式）")] string raceDate
[Description("検索対象サイトのドメイン（例: www.jra.go.jp）省略可")] string? site = null
[Description("読み込むページ数（既定値 3）")] int maxPages = 3
```

- 値の制約や期待する形式を明示する
- 省略可能なパラメータにはデフォルト値の意味を書く
- モデルが値を **どこから取得すべきか** を書く（「ツールが返した URL」）

### 5.5 複合ツール設計の判断基準

小規模モデルが「検索 → ページ読み → リンク探索 → 追加読み」を正しく実行できない場合、
複合ツール（1 呼び出しで複数ステップ）を提供する。

```
BrowserSearchAndRead = 検索 + ページ読み込み + サブリンク自動追跡
```

**統合する判断基準**: モデルが 2 ステップ以上を一貫して正しく実行できないなら統合する。

---

## 6. DI 登録の作り方

### 6.1 登録ファイル

`src/HorseRacingPrediction.Agents/Workflow/AgentServiceCollectionExtensions.cs` に拡張メソッドを追加する。

### 6.2 基本パターン

```csharp
public static IServiceCollection AddMyAgent(this IServiceCollection services)
{
    services.AddTransient<MyAgent>(sp =>
    {
        var chatClient = sp.GetRequiredService<IChatClient>();
        // ツールを組み立てる
        var myTools = sp.GetRequiredService<MyTools>();
        var webBrowserAgent = sp.GetRequiredService<WebBrowserAgent>();
        var tools = new List<AITool>(myTools.GetAITools())
        {
            webBrowserAgent.CreateAIFunction()  // Agent-as-Tool
        };
        return new MyAgent(chatClient, tools);
    });
    return services;
}
```

### 6.3 依存チェーンの例

```csharp
// AddWebBrowserAgent(): 低レベル → 高レベルの順に登録
services.AddTransient<PlaywrightTools>();
services.AddTransient<WebBrowserAgent>(sp => {
    var chatClient = sp.GetRequiredService<IChatClient>();
    var browser = sp.GetRequiredService<IWebBrowser>();
    var options = sp.GetRequiredService<IOptions<WebFetchOptions>>();
    var playwrightTools = new PlaywrightTools(browser, options);
    return new WebBrowserAgent(chatClient, playwrightTools.GetAITools());
});
services.AddTransient<WebFetchTools>(sp => new WebFetchTools(sp.GetRequiredService<WebBrowserAgent>()));
services.AddTransient<HorseRacingTools>(sp => new HorseRacingTools(sp.GetRequiredService<WebFetchTools>()));
```

### 6.4 ツール合成パターン

複数のツールプラグインを 1 つのエージェントに渡す:

```csharp
services.AddTransient<HorseDataAgent>(sp =>
{
    var chatClient = sp.GetRequiredService<IChatClient>();
    var horseRacingTools = sp.GetRequiredService<HorseRacingTools>();
    var webBrowserAgent = sp.GetRequiredService<WebBrowserAgent>();

    // HorseRacingTools の全ツール + WebBrowserAgent を 1 リストにまとめる
    var tools = new List<AITool>(horseRacingTools.GetAITools())
    {
        webBrowserAgent.CreateAIFunction()
    };
    return new HorseDataAgent(chatClient, tools);
});
```

---

## 7. プロジェクト全体のアーキテクチャ

### 7.1 レイヤー構成

```
PlaywrightWebBrowser (IWebBrowser)   ← ブラウザプリミティブ
    ↓
PlaywrightTools                      ← AI ツール（BrowserNavigate, BrowserGetLinks, BrowserSearch, BrowserSearchAndRead）
    ↓
WebBrowserAgent                      ← 自律エージェント（ChatClientAgent + PlaywrightTools）
    ↓
WebFetchTools                        ← 高レベル API（SearchWeb, ExploreFromEntryPoint, FetchPageContent, SearchAndFetch）
    ↓
HorseRacingTools                     ← ドメイン固有ツール
```

### 7.2 ドメイン非依存の原則

- **ブラウザ層（PlaywrightWebBrowser, PlaywrightTools, WebBrowserAgent）** にドメイン固有キーワードを入れない
- ドメイン固有ロジックは `HorseRacingTools` 以降のレイヤーに配置する
- `ScoreLinkCandidate` のキーワードは汎用的なもの（details, 一覧, table 等）のみ

---

## 8. エージェント作成手順（チュートリアル）

### Step 1: 目的の定義

エージェントが達成すべきタスクを 1 文で定義する。

例: 「指定された競走馬の詳細情報（血統・成績・適性）を Web 検索で収集し Markdown で返す」

### Step 2: ツールの選定

既存ツールで足りるか確認する。足りなければ §5 に従って新規作成する。

| 既存ツール | 用途 |
|-----------|------|
| `PlaywrightTools` | WebBrowserAgent 専用のブラウザ操作 |
| `WebFetchTools` | 汎用 Web 検索・ページ取得 |
| `HorseRacingTools` | 競馬固有の情報取得 |
| `CalendarTools` | 日付・曜日計算 |
| `RaceQueryTools` | EventFlow ReadModel クエリ |
| `PredictionWriteTools` | 予測チケット作成・確定 |

### Step 3: システムプロンプトの作成

§3 のテンプレートに従う。テーブル形式ツール選択 + 5 ステップ以下の手順 + 3〜5 個のルール。

### Step 4: エージェントクラスの実装

§4 のパターン A/B/C から適切なものを選び、`Agents/` ディレクトリに配置する。

### Step 5: DI 登録

§6 に従い `AgentServiceCollectionExtensions.cs` に登録メソッドを追加する。

### Step 6: テスト

- ツール単体テスト: `FakeWebBrowser` や `Func<>` デリゲートでモック
- 統合テスト: `WebBrowserAgentVerifier` で実際のブラウザ + LLM で E2E 検証

### Step 7: 反復改善

1. Verifier の出力を観察する
2. モデルが間違えるパターンを特定する
3. **まずツール説明を改善する**（プロンプトより効果が高い）
4. 改善が不十分ならツール統合やサブリンク自動追跡などの機能追加を検討する

---

## 9. デバッグ手順

エージェントが期待どおり動かない場合:

1. **Verifier で実行** して、どのツール呼び出しで失敗しているか確認
2. **ツール結果を確認** — ツールが正しいデータを返しているか？
3. **ツール選択を確認** — モデルが適切なツールを選んでいるか？
4. **パラメータを確認** — モデルが正しい引数を渡しているか？

| 症状 | よくある原因 | 対策 |
|------|------------|------|
| 間違ったツールを選ぶ | ツール説明が曖昧 | Description を具体化、テーブルで使い分け明示 |
| URL を推測する | プロンプトの制約不足 | 「ツールが返した URL だけ使う」ルール追加 |
| 検索後にページを読まない | 2 ステップの実行失敗 | `BrowserSearchAndRead` に統合 |
| 関連ページに到達しない | 1 ページ読んで終了 | サブリンク自動追跡機能を追加 |
| ノイズリンクを取得 | 検索結果フィルタ不足 | `IsExcludedSearchResultUrl` / `ScoreLinkCandidate` を改善 |

---

## チェックリスト

### エージェント作成

- [ ] エージェントの目的を 1 文で定義した
- [ ] 必要なツールを特定した（既存 or 新規作成）
- [ ] `const string SystemPrompt` にテーブル形式ツール選択を含めた
- [ ] ルールは 5 個以下に絞った
- [ ] `sealed class` + `ChatClientAgent` ラッパーで実装した
- [ ] 公開 API を適切なパターン（A/B/C）で設計した
- [ ] `AgentServiceCollectionExtensions.cs` に DI 登録を追加した

### ツール作成

- [ ] `sealed class` + `GetAITools()` で実装した
- [ ] 各メソッドに `[Description("何+いつ+違い")]` を付与した
- [ ] 各パラメータに `[Description("制約+形式")]` を付与した
- [ ] 戻り値は `string`（LLM が読むテキスト）
- [ ] テスト用にモック可能な設計にした（`internal` コンストラクタ or `Func<>` デリゲート）
- [ ] ドメイン固有ロジックをブラウザ層に入れていない

### 検証

- [ ] FakeWebBrowser / デリゲートでユニットテストを作成した
- [ ] Verifier で E2E 動作確認した
- [ ] `dotnet build` エラー 0、`dotnet test` 全テスト成功
