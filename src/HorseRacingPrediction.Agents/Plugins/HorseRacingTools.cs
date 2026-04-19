using System.ComponentModel;
using System.Text;
using System.Web;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Plugins;

/// <summary>
/// 競馬情報の取得に特化したプラグイン。
/// JRA 公式サイトや netkeiba から出馬表・戦績・騎手成績・調教師成績などを取得する。
/// 内部では <see cref="WebFetchTools"/> を使用して Web アクセスを行う。
/// <see cref="GetAITools"/> で <see cref="AITool"/> 一覧を取得し、
/// <see cref="Microsoft.Agents.AI.ChatClientAgent"/> に渡すことで利用可能になる。
/// </summary>
public sealed class HorseRacingTools
{
    private readonly WebFetchTools _webTools;

    public HorseRacingTools(WebFetchTools webTools)
    {
        _webTools = webTools;
    }

    /// <summary>
    /// 指定した競馬場・日付・レース番号の出馬表を取得して Markdown 形式で返す。
    /// </summary>
    [Description("指定した競馬場・日付・レース番号の出馬表（出走馬情報）を Markdown 表形式で取得します。")]
    public async Task<string> FetchRaceCard(
        [Description("競馬場コード（例: 05 = 東京、06 = 中山）")] string racecourseCode,
        [Description("レース開催日（YYYYMMDD 形式）")] string raceDate,
        [Description("レース番号（1〜12）")] int raceNumber,
        CancellationToken cancellationToken = default)
    {
        var url = BuildJraRaceCardUrl(racecourseCode, raceDate, raceNumber);
        var rawText = await _webTools.FetchPageContent(url, cancellationToken);
        return $"# 出馬表 競馬場={racecourseCode} 日付={raceDate} R{raceNumber}\n\n{rawText}";
    }

    /// <summary>
    /// 指定したレース名について、JRA 公式サイトの出走馬情報ページを優先して取得する。
    /// </summary>
    [Description("指定したレース名について、JRA公式サイトの出走馬情報を優先して取得します。出走馬一覧の確認に使います。")]
    public async Task<string> FetchJraEntryList(
        [Description("レース名（例: 皐月賞、有馬記念）")] string raceName,
        CancellationToken cancellationToken = default)
    {
        var content = await _webTools.SearchAndFetchContentAsync(
            $"{raceName} 出走馬情報",
            "www.jra.go.jp",
            maxLinksToFetch: 1,
            cancellationToken: cancellationToken);

        return FormatSection($"JRA公式の{raceName}出走馬情報", content);
    }

    /// <summary>
    /// 指定した馬名で netkeiba を検索し、過去の出走・成績情報を取得する。
    /// </summary>
    [Description("指定した馬名の過去の出走成績（戦績）を取得します。")]
    public async Task<string> FetchHorseHistory(
        [Description("馬名（日本語可）")] string horseName,
        CancellationToken cancellationToken = default)
    {
        var encoded = HttpUtility.UrlEncode(horseName);
        var url = $"https://db.netkeiba.com/horse/search/?name={encoded}";
        var rawText = await _webTools.FetchPageContent(url, cancellationToken);
        return FormatSection($"馬名「{horseName}」の戦績", rawText);
    }

    /// <summary>
    /// 指定した騎手名の最近の成績・勝率を取得する。
    /// </summary>
    [Description("指定した騎手名の最近の成績・騎乗数・勝率を取得します。")]
    public async Task<string> FetchJockeyStats(
        [Description("騎手名（日本語可）")] string jockeyName,
        CancellationToken cancellationToken = default)
    {
        var encoded = HttpUtility.UrlEncode(jockeyName);
        var url = $"https://db.netkeiba.com/jockey/search/?name={encoded}";
        var rawText = await _webTools.FetchPageContent(url, cancellationToken);
        return FormatSection($"騎手「{jockeyName}」の成績", rawText);
    }

    /// <summary>
    /// 指定した調教師名の成績・厩舎情報を取得する。
    /// </summary>
    [Description("指定した調教師名（厩舎）の成績・勝率・管理馬情報を取得します。")]
    public async Task<string> FetchTrainerStats(
        [Description("調教師名（日本語可）")] string trainerName,
        CancellationToken cancellationToken = default)
    {
        var encoded = HttpUtility.UrlEncode(trainerName);
        var url = $"https://db.netkeiba.com/trainer/search/?name={encoded}";
        var rawText = await _webTools.FetchPageContent(url, cancellationToken);
        return FormatSection($"調教師「{trainerName}」の成績・厩舎情報", rawText);
    }

    /// <summary>
    /// 指定したレース名・年度の過去レース結果を取得する。
    /// </summary>
    [Description("指定したレース名・年度の過去レース結果（着順・タイム・配当）を取得します。")]
    public async Task<string> FetchRaceResults(
        [Description("レース名（日本語可、例: 天皇賞秋）")] string raceName,
        [Description("年度（例: 2024、省略時は最新）")] string? year = null,
        CancellationToken cancellationToken = default)
    {
        var query = string.IsNullOrWhiteSpace(year)
            ? $"{raceName} レース結果"
            : $"{raceName} {year}年 レース結果";
        var content = await _webTools.SearchAndFetchContentAsync(query, "db.netkeiba.com", cancellationToken: cancellationToken);
        return FormatSection($"レース「{raceName}」{(year != null ? year + "年" : "")}の結果", content);
    }

    /// <summary>
    /// このプラグインのメソッドを <see cref="AITool"/> 一覧として返す。
    /// </summary>
    public IList<AITool> GetAITools() =>
    [
        AIFunctionFactory.Create(FetchRaceCard),
        AIFunctionFactory.Create(FetchJraEntryList),
        AIFunctionFactory.Create(FetchHorseHistory),
        AIFunctionFactory.Create(FetchJockeyStats),
        AIFunctionFactory.Create(FetchTrainerStats),
        AIFunctionFactory.Create(FetchRaceResults)
    ];

    // ------------------------------------------------------------------ //
    // private helpers
    // ------------------------------------------------------------------ //

    private static string BuildJraRaceCardUrl(string racecourseCode, string raceDate, int raceNumber)
    {
        // JRA の出馬表 URL パターン
        // CNAME 形式: pw01sde0203_{raceDate}{racecourseCode}{raceNumber:00}01
        //   pw01sde0203_ : JRA の出馬表画面識別子（固定値）
        //   raceDate      : 開催日（YYYYMMDD）
        //   racecourseCode: 競馬場コード（例: 05=東京, 06=中山）
        //   raceNumber    : レース番号（2桁ゼロ埋め）
        //   01            : 回次・日次（参照用固定値）
        return $"https://www.jra.go.jp/JRADB/accessD.html?" +
               $"CNAME=pw01sde0203_{raceDate}{racecourseCode}{raceNumber:D2}01&sub=";
    }

    private static string FormatSection(string heading, string body)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {heading}");
        sb.AppendLine();
        sb.Append(body);
        return sb.ToString();
    }
}
