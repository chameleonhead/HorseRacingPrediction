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

    public JraScrapingTools(JraRaceCardScraper raceCardScraper)
    {
        _raceCardScraper = raceCardScraper;
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
    /// このプラグインのメソッドを <see cref="AITool"/> 一覧として返す。
    /// </summary>
    public IList<AITool> GetAITools() =>
    [
        AIFunctionFactory.Create(ScrapeJraRaceCard)
    ];
}
