# エージェント作成の詳細パターン

`SKILL.md` §4 の補足。具体的なコード例とパターン解説。

---

## パターン A: テキスト出力エージェント（最も一般的）

Markdown テキストを返す。`HorseDataAgent`, `JockeyDataAgent`, `RaceDataAgent`, `StableDataAgent` が該当。

### 完全な実装例（HorseDataAgent）

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Agents;

public sealed class HorseDataAgent
{
    public const string AgentName = "HorseDataAgent";

    public const string SystemPrompt = """
        あなたは競走馬の情報を収集する専門エージェントです。
        指定された馬について、インターネット（netkeiba・JRA 公式など）から
        以下の情報を収集し、Markdown 形式で返してください。

        ## 収集する情報
        1. **基本情報**: 馬名・性別・馬齢・毛色・生年月日・馬主・生産者
        2. **血統**: 父・母・母父（サイアーライン）
        3. **過去成績**: 直近10走の着順・レース名・距離・馬場・タイム・着差・騎手・斤量
        4. **コース・距離適性**: 芝/ダート別、距離帯別の勝率・連対率
        5. **馬場状態適性**: 良・稍重・重・不良別の成績
        6. **調教情報**: 最新の調教タイム・コース・評価
        7. **近走特記事項**: 出遅れ・不利・馬体重増減などの注目点

        ## 行動方針
        - `BrowseWeb` ツールに取得したい情報を自然言語で依頼してインターネットから情報を取得する
        - 取得できなかった項目は「不明」と記載し、他の項目を続ける

        ## 出力形式
        Markdown 形式。見出しは ## と ### を使用し、成績は表形式で記載してください。
        """;

    private readonly ChatClientAgent _innerAgent;

    public HorseDataAgent(IChatClient chatClient, IList<AITool> tools)
    {
        _innerAgent = new ChatClientAgent(
            chatClient,
            name: AgentName,
            instructions: SystemPrompt,
            tools: tools);
    }

    public async Task<string> CollectAsync(
        string horseName, CancellationToken cancellationToken = default)
    {
        var result = await _innerAgent.RunAsync(
            $"競走馬「{horseName}」の詳細情報を収集してください。",
            cancellationToken: cancellationToken);
        return result.Text;
    }
}
```

**ポイント**:
- 公開メソッド名はタスクを表す動詞にする（`CollectAsync`, `AnalyzeAsync`, `PredictAsync`）
- プロンプトにユーザー入力をテンプレートリテラルで埋め込む
- 出力形式をシステムプロンプトで指定

---

## パターン B: Agent-as-Tool パターン（他エージェントのツールになる）

`WebBrowserAgent` がこのパターンを使用。自分自身を `AIFunction` として公開し、
他のエージェントから `BrowseWeb` ツールとして呼び出せるようにする。

### 完全な実装例（WebBrowserAgent）

```csharp
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Agents.Agents;

public sealed class WebBrowserAgent
{
    public const string AgentName = "WebBrowserAgent";

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

    private readonly ChatClientAgent _innerAgent;

    public WebBrowserAgent(IChatClient chatClient, IList<AITool> tools)
    {
        _innerAgent = new ChatClientAgent(
            chatClient, name: AgentName, instructions: SystemPrompt, tools: tools);
    }

    // 直接呼び出し用
    public async Task<string> InvokeAsync(
        string userMessage, CancellationToken cancellationToken = default)
    {
        var result = await _innerAgent.RunAsync(userMessage, cancellationToken: cancellationToken);
        return result.Text;
    }

    // ★ 他エージェントのツールとして公開
    public AIFunction CreateAIFunction() => _innerAgent.AsAIFunction();

    // ★ DI ファクトリメソッド
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

### 利用側でのツール合成

```csharp
// HorseDataAgent の DI 登録
services.AddTransient<HorseDataAgent>(sp =>
{
    var chatClient = sp.GetRequiredService<IChatClient>();
    var horseRacingTools = sp.GetRequiredService<HorseRacingTools>();
    var webBrowserAgent = sp.GetRequiredService<WebBrowserAgent>();

    // HorseRacingTools の全ツール + WebBrowserAgent を 1 リストにまとめる
    var tools = new List<AITool>(horseRacingTools.GetAITools())
    {
        webBrowserAgent.CreateAIFunction()  // エージェントをツールとして追加
    };
    return new HorseDataAgent(chatClient, tools);
});
```

---

## パターン C: 構造化出力エージェント（JSON → DTO に変換）

`WeekendRaceDiscoveryAgent` がこのパターンを使用。エージェントの応答 JSON をパースして型付きオブジェクトを返す。

### 実装のポイント

```csharp
public sealed class WeekendRaceDiscoveryAgent
{
    public const string SystemPrompt = """
        ...
        **必ず** 以下の JSON 配列形式のみで返してください。
        説明文・Markdown・コードブロック以外のテキストは一切含めないこと。

        ## 出力形式（JSON 配列）
        ```json
        [
          {
            "raceName": "レース名",
            "raceDate": "YYYY-MM-DD",
            "racecourse": "競馬場名",
            "raceNumber": 11,
            "raceQuery": "YYYY年レース名 競馬場NR",
            "horseNames": ["馬名1", "馬名2"],
            "jockeyNames": ["騎手名1"],
            "trainerNames": ["調教師名1"]
          }
        ]
        ```
        ...
        """;

    // 型付きオブジェクトを返す公開 API
    public async Task<IReadOnlyList<WeekendRaceInfo>> DiscoverAsync(
        DateOnly targetWeekend, CancellationToken ct = default)
    {
        var prompt = $"...のレースを JSON 配列で返してください。";
        var result = await _innerAgent.RunAsync(prompt, cancellationToken: ct);
        return ParseRaceInfoList(result.Text);  // JSON → DTO 変換
    }

    // JSON パーサー（エラー耐性あり）
    private static IReadOnlyList<WeekendRaceInfo> ParseRaceInfoList(string text)
    {
        var jsonText = ExtractJsonArray(text);
        if (string.IsNullOrWhiteSpace(jsonText)) return [];
        try
        {
            var dtos = JsonSerializer.Deserialize<List<WeekendRaceInfoDto>>(
                jsonText,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return dtos?.ConvertAll(ToWeekendRaceInfo) ?? [];
        }
        catch (JsonException) { return []; }  // LLM 出力は不安定なため
    }

    // ```json...``` ブロックと素の [...] の両方に対応
    private static string ExtractJsonArray(string text)
    {
        const string codeBlockStart = "```json";
        var startIdx = text.IndexOf(codeBlockStart, StringComparison.Ordinal);
        if (startIdx >= 0) { /* コードブロック抽出 */ }

        // 素の [ ... ] も探す
        var bracketStart = text.IndexOf('[');
        var bracketEnd = text.LastIndexOf(']');
        if (bracketStart >= 0 && bracketEnd > bracketStart)
            return text[bracketStart..(bracketEnd + 1)];

        return string.Empty;
    }
}
```

**必須テクニック**:
- システムプロンプトに JSON フォーマットの具体例を含める
- `ExtractJsonArray` で ` ```json ``` ` と素の `[...]` の両方に対応する
- `JsonException` を catch して空リストを返す（LLM 出力は不安定）
- 内部 DTO クラス → public record への変換メソッドを用意する
- `PropertyNameCaseInsensitive = true` で大文字小文字の揺れに対応

---

## DI 登録パターン

### 登録ファイル

`src/HorseRacingPrediction.Agents/Workflow/AgentServiceCollectionExtensions.cs`

### 依存チェーンの登録順

低レベル → 高レベルの順に登録する:

```csharp
public static IServiceCollection AddWebBrowserAgent(this IServiceCollection services)
{
    // 1. ツールプラグイン
    services.AddTransient<PlaywrightTools>();

    // 2. エージェント（ツールを注入）
    services.AddTransient<WebBrowserAgent>(sp =>
    {
        var chatClient = sp.GetRequiredService<IChatClient>();
        var browser = sp.GetRequiredService<IWebBrowser>();
        var options = sp.GetRequiredService<IOptions<WebFetchOptions>>();
        var playwrightTools = new PlaywrightTools(browser, options);
        return new WebBrowserAgent(chatClient, playwrightTools.GetAITools());
    });

    // 3. エージェント委譲ツール
    services.AddTransient<WebFetchTools>(sp =>
        new WebFetchTools(sp.GetRequiredService<WebBrowserAgent>()));

    // 4. ドメイン固有ツール
    services.AddTransient<HorseRacingTools>(sp =>
        new HorseRacingTools(sp.GetRequiredService<WebFetchTools>()));

    return services;
}
```

### ワークフロー登録

```csharp
public static IServiceCollection AddDataCollectionWorkflow(this IServiceCollection services)
{
    // 4 つのデータ収集エージェントそれぞれにツールを合成
    services.AddTransient<RaceDataAgent>(sp =>
    {
        var chatClient = sp.GetRequiredService<IChatClient>();
        var webBrowserAgent = sp.GetRequiredService<WebBrowserAgent>();
        var horseRacingTools = sp.GetRequiredService<HorseRacingTools>();
        var tools = new List<AITool>(horseRacingTools.GetAITools())
        {
            webBrowserAgent.CreateAIFunction()
        };
        return new RaceDataAgent(chatClient, tools);
    });
    // HorseDataAgent, JockeyDataAgent, StableDataAgent も同様...

    services.AddTransient<DataCollectionWorkflow>();
    return services;
}
```

### ファクトリメソッドパターン

DI を使わずにエージェントを構築する場合（Verifier やテスト用）:

```csharp
// WeeklyScheduleWorkflow のファクトリ
public static WeeklyScheduleWorkflow Create(
    IChatClient chatClient, WebBrowserAgent webBrowserAgent, CalendarTools? calendarTools = null)
{
    calendarTools ??= new CalendarTools();
    var browseWebTool = webBrowserAgent.CreateAIFunction();
    var discoveryTools = new List<AITool>(calendarTools.GetAITools()) { browseWebTool };
    var predictionTools = new List<AITool> { browseWebTool };

    return new WeeklyScheduleWorkflow(
        new WeekendRaceDiscoveryAgent(chatClient, discoveryTools),
        DataCollectionWorkflow.Create(chatClient, webBrowserAgent),
        new PostPositionPredictionAgent(chatClient, predictionTools));
}
```
