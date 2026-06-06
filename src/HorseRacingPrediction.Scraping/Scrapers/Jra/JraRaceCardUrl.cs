using System.Text.RegularExpressions;

namespace HorseRacingPrediction.Scraping.Scrapers.Jra;

/// <summary>
/// JRA 公式サイトで発見した出馬表ページの URL と、
/// URL に埋め込まれたメタ情報（開催日・競馬場コード・レース番号）を保持する。
/// <para>
/// JRA の出馬表 URL は CNAME パラメータに
/// <c>pw01sde0203_{YYYYMMDD}{CC}{NN}01</c> の形式で開催情報が含まれており、
/// <see cref="ParseFromUrl"/> でパースできる。
/// </para>
/// </summary>
public sealed record JraRaceCardUrl(
    /// <summary>出馬表ページの完全 URL</summary>
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
    // CNAME 形式: pw01sde0203_{YYYYMMDD}{CC}{NN}01
    //   YYYYMMDD: 開催日
    //   CC      : 競馬場コード（2桁）
    //   NN      : レース番号（2桁）
    //   01      : 回次・日次（固定値）
    private static readonly Regex DirectCnameRegex =
        new(@"pw01sde0203_(\d{4})(\d{2})(\d{2})(\d{2})(\d{2})\d{2}", RegexOptions.Compiled);

    // CNAME 形式: pw01dde1{0CC}{YYYY}{KK}{DD}{NN}{YYYY}{MM}{DD}
    //   0CC     : 競馬場コード（3桁のうち後ろ2桁を使用）
    //   YYYYMMDD: 開催日
    //   NN      : レース番号（2桁）
    private static readonly Regex ThisWeekCnameRegex =
        new(@"pw01dde1(\d{3})(\d{4})(\d{2})(\d{2})(\d{2})(\d{4})(\d{2})(\d{2})", RegexOptions.Compiled);

    /// <summary>
    /// JRA 出馬表 URL から <see cref="JraRaceCardUrl"/> を生成する。
    /// CNAME パラメータが含まれる URL であれば、開催日・競馬場コード・レース番号を自動解析する。
    /// </summary>
    /// <param name="url">JRA 出馬表ページの URL</param>
    /// <param name="racecourse">競馬場名（日本語）。発見エージェントが返した場合に設定する</param>
    public static JraRaceCardUrl ParseFromUrl(string url, string? racecourse = null)
    {
        var directMatch = DirectCnameRegex.Match(url);
        if (directMatch.Success)
        {
            return BuildFromDirectMatch(url, racecourse, directMatch);
        }

        var thisWeekMatch = ThisWeekCnameRegex.Match(url);
        if (thisWeekMatch.Success)
        {
            return BuildFromThisWeekMatch(url, racecourse, thisWeekMatch);
        }

        if (!url.Contains("/syutsuba", StringComparison.OrdinalIgnoreCase))
        {
            return new JraRaceCardUrl(url, racecourse, null, null, null);
        }

        return new JraRaceCardUrl(url, racecourse, null, null, null);
    }

    private static JraRaceCardUrl BuildFromDirectMatch(string url, string? racecourse, Match match)
    {
        var raceDate = TryBuildDate(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
        var racecourseCode = match.Groups[4].Value;
        int? raceNumber = int.TryParse(match.Groups[5].Value, out var rn) ? rn : null;

        return new JraRaceCardUrl(url, racecourse, racecourseCode, raceDate, raceNumber);
    }

    private static JraRaceCardUrl BuildFromThisWeekMatch(string url, string? racecourse, Match match)
    {
        var raceDate = TryBuildDate(match.Groups[6].Value, match.Groups[7].Value, match.Groups[8].Value);
        var racecourseCode = match.Groups[1].Value[1..];
        int? raceNumber = int.TryParse(match.Groups[5].Value, out var rn) ? rn : null;

        return new JraRaceCardUrl(url, racecourse, racecourseCode, raceDate, raceNumber);
    }

    private static DateOnly? TryBuildDate(string yearText, string monthText, string dayText)
    {
        if (int.TryParse(yearText, out var year) &&
            int.TryParse(monthText, out var month) &&
            int.TryParse(dayText, out var day))
        {
            try
            {
                return new DateOnly(year, month, day);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        return null;
    }
}
