using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using HorseRacingPrediction.Agents.Scrapers.Jra;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Plugins;

/// <summary>
/// JRA サイト固有のスクレイピングツールを提供するプラグイン。
/// <para>
/// AIエージェントがページURLを特定した後、このプラグインのツールを使って
/// 出馬表などの構造化データを抽出するワークフローを想定している。
/// </para>
/// <para>
/// 依存関係: <see cref="JraRaceCardScraper"/> → <see cref="Browser.IWebBrowser"/>
/// </para>
/// </summary>
public sealed class JraScrapingTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly JraRaceCardScraper _raceCardScraper;
    private readonly JraPageExtractionTools _pageExtractionTools;

    public JraScrapingTools(JraRaceCardScraper raceCardScraper, JraPageExtractionTools pageExtractionTools)
    {
        _raceCardScraper = raceCardScraper;
        _pageExtractionTools = pageExtractionTools;
    }

    /// <summary>
    /// 指定した JRA 出馬表ページの URL から出走馬情報を構造化データとして抽出する。
    /// </summary>
    [Description("JRA 公式サイトの出馬表ページ URL を指定して、レース情報・出走馬一覧（馬名・騎手・斤量・枠番・馬体重・調教師など）を JSON 形式で取得します。AIがページ URLを特定した後に呼び出してください。")]
    public async Task<string> ScrapeJraRaceCard(
        [Description("JRA 出馬表ページの URL（例: https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01sde0203_...）")] string url,
        CancellationToken cancellationToken = default)
    {
        var result = await _raceCardScraper.ScrapeAsync(url, cancellationToken);
        if (result is null)
        {
            return "出馬表の取得に失敗しました。";
        }

        return JsonSerializer.Serialize(result, JsonOptions);
    }

    /// <summary>
    /// JRA 専用セッションを開始してページを開く。
    /// </summary>
    [Description("JRA専用セッションを開始してURLを開きます。以後の操作は同一セッションで継続されます。")]
    public Task<string> OpenJraPage(
        [Description("開くURL（例: https://www.jra.go.jp/keiba/thisweek/）")] string url,
        CancellationToken cancellationToken = default)
        => _pageExtractionTools.OpenJraPage(url, cancellationToken);

    /// <summary>
    /// 現在ページから最短導線でターゲット情報を抽出する。
    /// </summary>
    [Description("現在ページから最短導線で target の情報を抽出します。target=odds/race_card/snapshot。")]
    public Task<string> ExtractFromCurrentPage(
        [Description("抽出ターゲット。odds / race_card / snapshot")] string target = "snapshot",
        [Description("最大クリック回数。既定2")] int maxSteps = 2,
        CancellationToken cancellationToken = default)
        => _pageExtractionTools.ExtractFromCurrentPage(target, maxSteps, cancellationToken);

    /// <summary>
    /// 現在ページからオッズ画面への最短遷移を行う。
    /// </summary>
    [Description("現在ページからオッズ画面へ最短遷移し、ページ要約を返します。")]
    public Task<string> NavigateToOddsFromCurrentPage(
        [Description("最大クリック回数。既定2")] int maxSteps = 2,
        CancellationToken cancellationToken = default)
        => _pageExtractionTools.NavigateToOddsFromCurrentPage(maxSteps, cancellationToken);

    /// <summary>
    /// JRA専用セッションを終了する。
    /// </summary>
    [Description("JRA専用Playwrightセッションを終了します。")]
    public Task<string> CloseJraSession()
        => _pageExtractionTools.CloseJraSession();

    /// <summary>
    /// このプラグインのメソッドを <see cref="AITool"/> 一覧として返す。
    /// </summary>
    public IList<AITool> GetAITools() =>
    [
        AIFunctionFactory.Create(ScrapeJraRaceCard),
        AIFunctionFactory.Create(OpenJraPage),
        AIFunctionFactory.Create(ExtractFromCurrentPage),
        AIFunctionFactory.Create(NavigateToOddsFromCurrentPage),
        AIFunctionFactory.Create(CloseJraSession)
    ];
}
