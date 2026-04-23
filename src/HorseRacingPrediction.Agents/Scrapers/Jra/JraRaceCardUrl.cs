using System.Text.RegularExpressions;

namespace HorseRacingPrediction.Agents.Scrapers.Jra;

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
    private static readonly Regex CnameRegex =
        new(@"pw01sde0203_(\d{4})(\d{2})(\d{2})(\d{2})(\d{2})\d{2}", RegexOptions.Compiled);

    /// <summary>
    /// JRA 出馬表 URL から <see cref="JraRaceCardUrl"/> を生成する。
    /// CNAME パラメータが含まれる URL であれば、開催日・競馬場コード・レース番号を自動解析する。
    /// </summary>
    /// <param name="url">JRA 出馬表ページの URL</param>
    /// <param name="racecourse">競馬場名（日本語）。発見エージェントが返した場合に設定する</param>
    public static JraRaceCardUrl ParseFromUrl(string url, string? racecourse = null)
    {
        var match = CnameRegex.Match(url);
        if (!match.Success)
        {
            return new JraRaceCardUrl(url, racecourse, null, null, null);
        }

        DateOnly? raceDate = null;
        if (int.TryParse(match.Groups[1].Value, out var year) &&
            int.TryParse(match.Groups[2].Value, out var month) &&
            int.TryParse(match.Groups[3].Value, out var day))
        {
            try
            {
                raceDate = new DateOnly(year, month, day);
            }
            catch (ArgumentOutOfRangeException)
            {
                // invalid date — leave as null
            }
        }

        var racecourseCode = match.Groups[4].Value;
        int? raceNumber = int.TryParse(match.Groups[5].Value, out var rn) ? rn : null;

        return new JraRaceCardUrl(url, racecourse, racecourseCode, raceDate, raceNumber);
    }
}
