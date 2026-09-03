using System.Text.RegularExpressions;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Scraping.Jra.Parsing;

/// <summary>
/// JRA出馬表ページを解析する。馬番列を持つテーブルを対象とする。
/// 実ページの具体的なURL構造は未調査のため、テーブルの見出し（ヘッダー）から
/// 列を特定する方式にして、ページ構造変更への耐性を優先している。
/// </summary>
public sealed class RaceCardPageParser
    : IJraPageParser
{
    private static readonly Regex DateRegex =
        new(@"(?<year>\d{4})年\s*(?<month>\d{1,2})月\s*(?<day>\d{1,2})日", RegexOptions.Compiled);

    // Task16実サイト確認で判明: 実ページの見出しは「1レース」のように「R」ではなく
    // 「レース」表記であり、旧正規表現（digit+"R"）は常にマッチしなかった。
    private static readonly Regex RaceNumberRegex =
        new(@"(?<num>\d{1,2})\s*(?:R|レース)", RegexOptions.Compiled);

    private static readonly Regex TimeRegex =
        new(@"(?<hour>\d{1,2}):(?<minute>\d{2})", RegexOptions.Compiled);

    private static readonly Regex LeadingNumberRegex =
        new(@"(?<num>\d{1,2})", RegexOptions.Compiled);

    private static readonly Regex WeightRegex =
        new(@"(?<weight>\d{1,3}(\.\d)?)", RegexOptions.Compiled);

    public JraPageKind Kind =>
        JraPageKind.RaceCard;

    public int Priority => 90;

    public bool CanParse(
        PageSnapshot snapshot)
    {
        return FindEntryTable(snapshot) is not null;
    }

    public IJraPage Parse(
        PageSnapshot snapshot)
    {
        var table =
            FindEntryTable(snapshot)
            ?? throw new JraPageParseException(
                JraPageKind.RaceCard,
                snapshot.Url,
                "出馬表テーブルを取得できませんでした。");

        var date =
            ParseDate(snapshot);

        var course =
            ParseCourse(snapshot);

        var number =
            ParseRaceNumber(snapshot);

        var raceId =
            new RaceId(date, course, number);

        var raceName =
            ParseRaceName(snapshot);

        var startTime =
            ParseStartTime(snapshot);

        var entries =
            ParseEntries(table);

        return new JraRaceCardPage(
            snapshot.Url,
            raceId,
            raceName,
            startTime,
            entries);
    }

    private static PageTableSnapshot? FindEntryTable(
        PageSnapshot snapshot)
    {
        foreach (var table in snapshot.Tables)
        {
            if (FindHorseNumberColumnIndex(table.Headers) < 0)
            {
                continue;
            }

            if (FindHorseNameColumnIndex(table.Headers) < 0)
            {
                continue;
            }

            // 着順列を持つ場合はレース結果テーブルであり、出馬表ではない。
            if (table.Headers.Any(h => h.Contains("着順", StringComparison.Ordinal)))
            {
                continue;
            }

            return table;
        }

        return null;
    }

    private static int FindHorseNumberColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("馬番", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindFrameNumberColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("枠番", StringComparison.Ordinal) ||
                headers[i].Contains("枠", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindHorseNameColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("馬名", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindJockeyColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("騎手", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindAssignedWeightColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            // Task16実サイト確認で判明: 出馬表テーブルには「斤量」単独の列は無く、
            // 「性齢/毛色 負担重量 騎手名」という結合列に含まれる（「負担重量」表記）。
            if (headers[i].Contains("斤量", StringComparison.Ordinal) ||
                headers[i].Contains("負担重量", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static DateOnly ParseDate(
        PageSnapshot snapshot)
    {
        var searchText =
            $"{snapshot.Title} {string.Join(" ", snapshot.Headings)}";

        var match =
            DateRegex.Match(searchText);

        if (!match.Success)
        {
            throw new JraPageParseException(
                JraPageKind.RaceCard,
                snapshot.Url,
                "対象日付を取得できませんでした。");
        }

        return new DateOnly(
            int.Parse(match.Groups["year"].Value),
            int.Parse(match.Groups["month"].Value),
            int.Parse(match.Groups["day"].Value));
    }

    private static RaceCourse ParseCourse(
        PageSnapshot snapshot)
    {
        var searchText =
            $"{snapshot.Title} {string.Join(" ", snapshot.Headings)}";

        var course =
            RaceCourseNames.Parse(searchText);

        if (course == RaceCourse.Unknown)
        {
            throw new JraPageParseException(
                JraPageKind.RaceCard,
                snapshot.Url,
                "対象競馬場を取得できませんでした。");
        }

        return course;
    }

    private static int ParseRaceNumber(
        PageSnapshot snapshot)
    {
        var searchText =
            $"{snapshot.Title} {string.Join(" ", snapshot.Headings)}";

        var match =
            RaceNumberRegex.Match(searchText);

        if (!match.Success)
        {
            throw new JraPageParseException(
                JraPageKind.RaceCard,
                snapshot.Url,
                "対象レース番号を取得できませんでした。");
        }

        return int.Parse(match.Groups["num"].Value);
    }

    private static string? ParseRaceName(
        PageSnapshot snapshot)
    {
        foreach (var heading in snapshot.Headings)
        {
            var withoutNumber =
                RaceNumberRegex.Replace(heading, string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(withoutNumber) &&
                RaceCourseNames.Parse(withoutNumber) == RaceCourse.Unknown &&
                !DateRegex.IsMatch(withoutNumber))
            {
                return withoutNumber;
            }
        }

        return null;
    }

    private static TimeOnly? ParseStartTime(
        PageSnapshot snapshot)
    {
        var searchText =
            $"{snapshot.Title} {string.Join(" ", snapshot.Headings)} {snapshot.MainText}";

        var match =
            TimeRegex.Match(searchText);

        if (!match.Success)
        {
            return null;
        }

        return new TimeOnly(
            int.Parse(match.Groups["hour"].Value),
            int.Parse(match.Groups["minute"].Value));
    }

    private static IReadOnlyList<RaceEntry> ParseEntries(
        PageTableSnapshot table)
    {
        var horseNumberIndex = FindHorseNumberColumnIndex(table.Headers);
        var frameNumberIndex = FindFrameNumberColumnIndex(table.Headers);
        var horseNameIndex = FindHorseNameColumnIndex(table.Headers);
        var jockeyIndex = FindJockeyColumnIndex(table.Headers);
        var assignedWeightIndex = FindAssignedWeightColumnIndex(table.Headers);

        var entries = new List<RaceEntry>();
        var sequentialNumber = 0;

        foreach (var row in table.Rows)
        {
            // Task16実サイト確認で判明: 抽出したテーブルの1行目にヘッダー行自体が
            // 重複して含まれることがある（rowspanの影響と見られる）。そのまま
            // 見出し文字列を1頭目として扱わないよう読み飛ばす。
            if (IsHeaderRow(row, table.Headers))
            {
                continue;
            }

            if (horseNameIndex < 0 || horseNameIndex >= row.Count ||
                string.IsNullOrWhiteSpace(row[horseNameIndex]))
            {
                continue;
            }

            // Task16実サイト確認で判明: 実ページの馬名セルは
            // 「馬名 調教師名(所属) 父：… 母：…」が1セルに結合されている
            // （馬名 調教師名 血統 という結合ヘッダーの通り）。カタカナの馬名には
            // 空白を含まないため、先頭の空白より前を馬名として切り出す。
            var horseName =
                row[horseNameIndex].Split(' ', 2)[0].Trim();

            if (string.IsNullOrWhiteSpace(horseName))
            {
                continue;
            }

            // Task16実サイト確認で判明: 枠・馬番のセルは色付きアイコン画像で
            // 描画されており、テキスト抽出結果が空になる（レース結果ページの
            // 「枠6緑」のようなテキスト表現とは異なる）。空の場合は出現順の
            // 連番を暫定的な馬番として使う。
            sequentialNumber++;

            var horseNumber = sequentialNumber;

            if (horseNumberIndex >= 0 && horseNumberIndex < row.Count)
            {
                var numberMatch =
                    LeadingNumberRegex.Match(row[horseNumberIndex]);

                if (numberMatch.Success)
                {
                    horseNumber = int.Parse(numberMatch.Groups["num"].Value);
                }
            }

            int? frameNumber = null;

            if (frameNumberIndex >= 0 && frameNumberIndex < row.Count)
            {
                var frameMatch =
                    LeadingNumberRegex.Match(row[frameNumberIndex]);

                if (frameMatch.Success)
                {
                    frameNumber = int.Parse(frameMatch.Groups["num"].Value);
                }
            }

            var jockeyName =
                jockeyIndex >= 0 && jockeyIndex < row.Count
                    ? ExtractJockeyName(row[jockeyIndex])
                    : null;

            decimal? assignedWeight = null;

            if (assignedWeightIndex >= 0 && assignedWeightIndex < row.Count)
            {
                var weightMatch =
                    WeightRegex.Match(row[assignedWeightIndex]);

                if (weightMatch.Success)
                {
                    assignedWeight = decimal.Parse(weightMatch.Groups["weight"].Value);
                }
            }

            entries.Add(new RaceEntry(
                horseNumber,
                horseName,
                frameNumber,
                jockeyName,
                assignedWeight));
        }

        return entries;
    }

    private static bool IsHeaderRow(
        IReadOnlyList<string> row,
        IReadOnlyList<string> headers)
        => row.Count > 0 && headers.Count > 0 &&
           string.Equals(row[0], headers[0], StringComparison.Ordinal);

    /// <summary>
    /// 「性齢/毛色 負担重量 騎手名」のように結合されたセルから騎手名だけを取り出す。
    /// 実ページでは "牡4/栗 58.0kg △坂口 智康" のように、体重を表す "kg" の直後に
    /// 手綱を示す記号（減量マーク）と騎手名が続く（Task16実サイト確認で判明）。
    /// </summary>
    private static string? ExtractJockeyName(string cell)
    {
        if (string.IsNullOrWhiteSpace(cell))
        {
            return null;
        }

        var kgIndex =
            cell.IndexOf("kg", StringComparison.OrdinalIgnoreCase);

        var rest =
            kgIndex >= 0
                ? cell[(kgIndex + 2)..]
                : cell;

        rest = rest.Trim().TrimStart('△', '▲', '☆', '★', '◇', '▽').Trim();

        return string.IsNullOrWhiteSpace(rest) ? null : rest;
    }
}
