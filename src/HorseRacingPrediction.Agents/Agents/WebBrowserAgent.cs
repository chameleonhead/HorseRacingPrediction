using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// <see cref="PlaywrightTools"/> を使用して Web 上の情報を調査する汎用エージェント。
/// 他のエージェントから <see cref="InvokeAsync"/> を呼び出すことで使用できる。
/// また、<see cref="CreateAIFunction"/> で Microsoft Agent Framework の
/// <see cref="AIFunction"/> として取得し、他エージェントのツールとして登録することもできる。
/// </summary>
public sealed class WebBrowserAgent
{
    public const string AgentName = "WebBrowserAgent";

    public const string SystemPrompt = """
        あなたは Web 調査を行うブラウザエージェントです。
        ブラウザツールを使って Web ページにアクセスし、
        目的に沿った情報を収集して返します。
        回答はすべて日本語で行ってください。

        ## 利用可能なツール
        - BrowserSearchAndRead: 検索して上位ページの本文を一括取得する（最優先で使う）
        - BrowserNavigate: 指定 URL のページ本文テキストを取得する
        - BrowserGetLinks: ページ内のリンク一覧を抽出する
        - BrowserSearch: 検索エンジンでリンク一覧だけ取得する（リンク確認用）

        ## 行動手順（必ずこの順序で実行すること）
        1. まず BrowserSearchAndRead で検索し、ページ本文を取得する
        2. 取得した本文に目的の情報がなければ、BrowserGetLinks でリンクを調べる
        3. 見つけたリンクを BrowserNavigate で読む
        4. 十分な情報が揃ったら Markdown で整理して返す

        ## 絶対に守るべきルール
        - URL を自分で推測・生成してはいけない。ツールが返した URL だけを使うこと
        - リンクのタイトルだけで情報を要約してはいけない
        - 必ずページ本文を読んでから回答すること
        - URL が直接指定されている場合は BrowserNavigate でアクセスする
        - 回答には必ずツールが返した URL のみを参照 URL として記載すること

        ## 出力形式
        - 日本語で回答する
        - 要点の要約を先頭に置く
        - 根拠となる情報を見出し・箇条書き・表で整理する
        - 参照した URL を明記する（ツールから取得した実在の URL のみ）
        """;

    private readonly ChatClientAgent _innerAgent;

    public WebBrowserAgent(IChatClient chatClient, IList<AITool> tools)
    {
        _innerAgent = new ChatClientAgent(
            chatClient,
            name: AgentName,
            instructions: SystemPrompt,
            tools: tools);
    }

    /// <summary>
    /// エージェントにメッセージを送り、応答を返す。
    /// </summary>
    /// <param name="userMessage">ユーザーからの依頼内容</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>エージェントの応答テキスト</returns>
    public async Task<string> InvokeAsync(
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        var result = await _innerAgent.RunAsync(userMessage, cancellationToken: cancellationToken);
        return result.Text;
    }

    /// <summary>
    /// このエージェントを他のエージェントのツールとして使用するための
    /// <see cref="AIFunction"/> を生成する。
    /// 返された function を <c>tools</c> 一覧に追加することで、
    /// どのエージェントからも <c>WebBrowserAgent_BrowseWeb</c> として呼び出せる。
    /// </summary>
    public AIFunction CreateAIFunction() => _innerAgent.AsAIFunction();

    /// <summary>
    /// DI コンテナから <see cref="WebBrowserAgent"/> を構築するファクトリメソッド。
    /// <see cref="PlaywrightTools"/> をツールとして登録し、
    /// エージェントが Playwright ベースのブラウザ操作を自律的に使用できるようにする。
    /// </summary>
    public static WebBrowserAgent CreateFromServices(IServiceProvider services)
    {
        var chatClient = services.GetRequiredService<IChatClient>();
        var browser = services.GetRequiredService<IWebBrowser>();
        var options = services.GetRequiredService<IOptions<WebFetchOptions>>();

        var playwrightTools = new PlaywrightTools(browser, options);
        return new WebBrowserAgent(chatClient, playwrightTools.GetAITools());
    }
}
