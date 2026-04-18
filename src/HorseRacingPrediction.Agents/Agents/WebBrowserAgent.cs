using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// Playwright を操作してインターネットから競馬情報を取得する自律型エージェント。
/// 他のエージェントから <see cref="InvokeAsync"/> を呼び出すことで使用できる。
/// また、<see cref="CreatePlugin"/> で Semantic Kernel プラグインとして取得し、
/// 他エージェントの Kernel に登録することもできる。
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

    private readonly ChatCompletionAgent _innerAgent;

    public WebBrowserAgent(Kernel kernel)
    {
        _innerAgent = new ChatCompletionAgent
        {
            Name = AgentName,
            Instructions = SystemPrompt,
            Kernel = kernel
        };
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
        var sb = new System.Text.StringBuilder();
        await foreach (var response in _innerAgent.InvokeAsync(
            userMessage,
            thread: null,
            options: null,
            cancellationToken: cancellationToken))
        {
            sb.Append(response.Message.Content);
        }

        return sb.ToString();
    }

    /// <summary>
    /// このエージェントを他のエージェントのツールとして使用するための
    /// <see cref="KernelPlugin"/> を生成する。
    /// 返された plugin を <c>kernel.Plugins.Add(plugin)</c> で登録することで、
    /// どのエージェントからも <c>WebBrowserAgent_BrowseWeb</c> ツールとして呼び出せる。
    /// </summary>
    public KernelPlugin CreatePlugin()
    {
        return KernelPluginFactory.CreateFromFunctions(
            pluginName: AgentName,
            description: "Playwright を使ってインターネットから競馬情報を取得するエージェント",
            functions: [CreateBrowseWebFunction()]);
    }

    private KernelFunction CreateBrowseWebFunction()
    {
        return KernelFunctionFactory.CreateFromMethod(
            method: async (string request, CancellationToken ct) =>
                await InvokeAsync(request, ct),
            functionName: "BrowseWeb",
            description: "競馬情報の取得を依頼する。レース・馬・騎手に関する情報を" +
                         "インターネットから検索して Markdown 形式で返す。",
            parameters:
            [
                new KernelParameterMetadata("request")
                {
                    Description = "取得したい情報の依頼内容（例: '2024年天皇賞秋の出馬表を取得して'）",
                    ParameterType = typeof(string),
                    IsRequired = true
                }
            ],
            returnParameter: new KernelReturnParameterMetadata
            {
                Description = "取得した競馬情報（Markdown 形式）",
                ParameterType = typeof(string)
            });
    }

    /// <summary>
    /// DI コンテナから <see cref="WebBrowserAgent"/> を構築するファクトリメソッド。
    /// </summary>
    public static WebBrowserAgent CreateFromServices(IServiceProvider services)
    {
        var kernel = services.GetRequiredService<Kernel>();
        var browser = services.GetRequiredService<IWebBrowser>();
        var options = services.GetRequiredService<IOptions<WebFetchOptions>>();

        var agentKernel = kernel.Clone();
        var webFetchTools = new WebFetchTools(browser, options);
        agentKernel.Plugins.AddFromObject(webFetchTools, pluginName: "WebFetch");

        return new WebBrowserAgent(agentKernel);
    }
}
