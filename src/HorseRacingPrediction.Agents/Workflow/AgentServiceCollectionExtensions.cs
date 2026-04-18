using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

namespace HorseRacingPrediction.Agents.Workflow;

/// <summary>
/// エージェント関連サービスを DI コンテナに登録する拡張メソッドを提供する。
/// </summary>
public static class AgentServiceCollectionExtensions
{
    /// <summary>
    /// WebBrowserAgent および WebFetchTools を DI コンテナに登録する。
    /// <para>
    /// 使用例（Program.cs または テスト初期化）:
    /// <code>
    /// builder.Services.AddWebBrowserAgent();
    /// builder.Services.Configure&lt;WebFetchOptions&gt;(
    ///     builder.Configuration.GetSection(WebFetchOptions.SectionName));
    /// </code>
    /// </para>
    /// </summary>
    public static IServiceCollection AddWebBrowserAgent(this IServiceCollection services)
    {
        services.AddTransient<WebFetchTools>();
        services.AddTransient<WebBrowserAgent>(sp =>
        {
            var kernel = sp.GetRequiredService<Kernel>();
            var browser = sp.GetRequiredService<IWebBrowser>();
            var options = sp.GetRequiredService<IOptions<WebFetchOptions>>();

            var agentKernel = kernel.Clone();
            var webFetchTools = new WebFetchTools(browser, options);
            agentKernel.Plugins.AddFromObject(webFetchTools, pluginName: "WebFetch");

            return new WebBrowserAgent(agentKernel);
        });
        return services;
    }

    /// <summary>
    /// 既存の <see cref="Kernel"/> に WebFetchTools プラグインを追加する。
    /// 他エージェントがブラウザ取得ツールを直接使えるようにする場合に利用する。
    /// </summary>
    public static Kernel AddWebFetchPlugin(
        this Kernel kernel,
        IWebBrowser browser,
        IOptions<WebFetchOptions> options)
    {
        var plugin = new WebFetchTools(browser, options);
        kernel.Plugins.AddFromObject(plugin, pluginName: "WebFetch");
        return kernel;
    }
}
