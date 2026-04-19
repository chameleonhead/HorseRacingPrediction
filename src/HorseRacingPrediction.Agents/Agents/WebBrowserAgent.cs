using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        ブラウザは Chrome を前提とし、検索はアドレスバーに直接クエリを入力して行います。
        ページ取得は常に単一ページごとに完結させ、ツール呼び出し間でブラウザ状態を前提にしません。
        収集した情報を日本語の Markdown で整理して返します。

        ## ツール一覧と使い方
        | ツール | 用途 |
        |--------|------|
        | BrowserSearch | Chrome のアドレスバー相当の検索を行い、検索結果ページの DOM からリンク一覧を取得する |
        | BrowserReadPage | 指定 URL の単一ページを読み取る。必要な場合だけ同じ呼び出し内で詳細表示を 1 回クリックする |

        ## 行動手順
        1. 必要なら、依頼を読んだ直後に何を確認すれば完了かを 2〜4 ステップの短い作業計画として整理してよい
        2. 既知の URL があれば BrowserReadPage を使う。URL が明示されていなければ、対象サイト名やドメイン名が依頼に含まれていても、まず BrowserSearch で検索結果一覧を取得する
        3. BrowserSearch が返したリンク一覧から、表示名と URL をそのまま使って候補ページを選ぶ
        4. BrowserReadPage で 1 ページずつ本文を読む。追加のページが必要なら別 URL で再度 BrowserReadPage を呼ぶ
        5. 十分な情報が揃ったら、参照した URL を明記しつつ Markdown で整理して返す

        ## 重要なルール
        - 作業計画は任意。書く場合でも 1 回だけ短くまとめ、その後はすぐに必要なツールを呼ぶ
        - 検索は BrowserSearch を使う。検索用 URL を自分で組み立てない
        - URL が明示されていない場合は、対象サイト名やドメイン名が依頼に書かれていても、いきなりそのサイトを開かず必ず検索結果一覧を先に取得する
        - ページ取得は BrowserReadPage の単一呼び出しで完結させる。前回の表示状態を前提にしない
        - BrowserReadPage が必要と判断した場合を除き、詳細表示のためのクリックを自分で指示しない
        - 検索結果のタイトルやスニペットだけで結論を出さない。実際に対象ページを開いて確認する
        - 同じ内容の作業計画を繰り返さない
        - URL を推測・生成しない。ツールが返した URL やリンクだけを使う
        - 参照した URL を回答に明記する
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
        var extractionAgent = services.GetService<PageDataExtractionAgent>();
        var logger = services.GetRequiredService<ILogger<PlaywrightTools>>();

        var playwrightTools = new PlaywrightTools(browser, options, extractionAgent, logger);
        return new WebBrowserAgent(chatClient, playwrightTools.GetAITools());
    }
}
