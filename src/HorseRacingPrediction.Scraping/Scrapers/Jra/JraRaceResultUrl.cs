using System.Text.RegularExpressions;

namespace HorseRacingPrediction.Scraping.Scrapers.Jra;

/// <summary>
/// JRA 公式サイトで発見した成績ページの URL と、
/// URL に埋め込まれたメタ情報（開催日・競馬場コード・レース番号）を保持する。
/// <para>
/// JRA の成績 URL は CNAME パラメータに
/// <c>pw01skd0203_{YYYYMMDD}{CC}{NN}01</c> の形式で開催情報が含まれており、
/// <see cref="ParseFromUrl"/> でパースできる。
/// </para>
/// </summary>
public sealed record JraRaceResultUrl(
    /// <summary>成績ページの完全 URL</summary>
    string Url,
    /// <summary>競馬場名（日本語、例: 東京）。発見エージェントが取得した場合のみ設定される</summary>
    string? Racecourse,
    /// <summary>競馬場コード（2桁数字、例: 05）。CNAME URL から解析した値</summary>
    string? RacecourseCode,
    /// <summary>開催日。CNAME URL から解析した値</summary>
    DateOnly? RaceDate,
    /// <summary>レース番号（1〜12）。CNAME URL から解析した値</summary>
    int? RaceNumber)
{
    // CNAME 形式: pw01skd0203_{YYYYMMDD}{CC}{NN}01
    //   YYYYMMDD: 開催日
    //   CC      : 競馬場コード（2桁）
    //   NN      : レース番号（2桁）
    //   01      : 回次・日次（固定値）
    private static readonly Regex ResultDetailRegex =
        new(@"pw01skd0203_(\d{4})(\d{2})(\d{2})(\d{2})(\d{2})\d{2}", RegexOptions.Compiled);

    private static readonly Regex ResultSelectionRegex =
        new(@"pw01sde1(\d{3})(\d{4})(\d{2})(\d{2})(\d{2})(\d{4})(\d{2})(\d{2})", RegexOptions.Compiled);

    /// <summary>
    /// JRA 成績 URL から <see cref="JraRaceResultUrl"/> を生成する。
    /// CNAME パラメータが含まれる URL であれば、開催日・競馬場コード・レース番号を自動解析する。
    /// </summary>
    /// <param name="url">JRA 成績ページの URL</param>
    /// <param name="racecourse">競馬場名（日本語）。発見エージェントが返した場合に設定する</param>
    public static JraRaceResultUrl ParseFromUrl(string url, string? racecourse = null)
    {
        var detailMatch = ResultDetailRegex.Match(url);
        if (detailMatch.Success)
        {
            return BuildFromDetailMatch(url, racecourse, detailMatch);
        }

        var selectionMatch = ResultSelectionRegex.Match(url);
        if (selectionMatch.Success)
        {
            return BuildFromSelectionMatch(url, racecourse, selectionMatch);
        }

        return new JraRaceResultUrl(url, racecourse, null, null, null);
    }

    private static JraRaceResultUrl BuildFromDetailMatch(string url, string? racecourse, Match match)
    {
        var raceDate = TryBuildDate(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
        var racecourseCode = match.Groups[4].Value;
        int? raceNumber = int.TryParse(match.Groups[5].Value, out var rn) ? rn : null;

        return new JraRaceResultUrl(url, racecourse, racecourseCode, raceDate, raceNumber);
    }

    private static JraRaceResultUrl BuildFromSelectionMatch(string url, string? racecourse, Match match)
    {
        var raceDate = TryBuildDate(match.Groups[6].Value, match.Groups[7].Value, match.Groups[8].Value);
        var racecourseCode = match.Groups[1].Value[1..];
        int? raceNumber = int.TryParse(match.Groups[5].Value, out var rn) ? rn : null;

        return new JraRaceResultUrl(url, racecourse, racecourseCode, raceDate, raceNumber);
    }

    private static DateOnly? TryBuildDate(string yearText, string monthText, string dayText)
    {
        if (!int.TryParse(yearText, out var year)
            || !int.TryParse(monthText, out var month)
            || !int.TryParse(dayText, out var day))
        {
            return null;
        }

        try
        {
            return new DateOnly(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
