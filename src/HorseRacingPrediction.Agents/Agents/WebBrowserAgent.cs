using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// Playwright を操作してインターネットから競馬情報を取得する自律型エージェント。
/// 他のエージェントから <see cref="InvokeAsync"/> を呼び出すことで使用できる。
/// また、<see cref="CreateAIFunction"/> で Microsoft Agent Framework の
/// <see cref="AIFunction"/> として取得し、他エージェントのツールとして登録することもできる。
/// </summary>
public sealed class WebBrowserAgent
{
    private const string AgentName = "WebBrowserAgent";

    private const string SystemPrompt = """
        あなたは競馬情報収集の専門エージェントです。
        Playwright ツールを使ってインターネットから競馬に関する情報を取得し、
        構造化された Markdown 形式で回答します。

        ## 行動方針
        - 利用可能なツール（FetchPageContent, SearchAndFetch, FetchRaceCard,
          FetchHorseHistory, FetchJockeyStats）を活用して情報を収集する
        - JRA（www.jra.go.jp）や netkeiba（db.netkeiba.com）など
          許可されたドメインの情報を優先する
        - ログインが必要なページや、過負荷になりそうなページへの
          連続アクセスは避ける
        - 取得した情報は必ず Markdown 形式（見出し・表・箇条書き）で整理して返す
        - 情報が取得できなかった場合はその旨を明記し、代替情報を提示する

        ## 出力形式
        - レース情報: ## レース情報 セクションに Markdown 表で出力
        - 馬情報: ## 馬情報 セクションに戦績・近走成績を記載
        - 騎手情報: ## 騎手情報 セクションに勝率・近走成績を記載
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
    /// </summary>
    public static WebBrowserAgent CreateFromServices(IServiceProvider services)
    {
        var chatClient = services.GetRequiredService<IChatClient>();
        var browser = services.GetRequiredService<IWebBrowser>();
        var options = services.GetRequiredService<IOptions<WebFetchOptions>>();

        var webFetchTools = new WebFetchTools(browser, options);
        return new WebBrowserAgent(chatClient, webFetchTools.GetAITools());
    }
}
