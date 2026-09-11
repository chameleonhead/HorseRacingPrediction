using System.Globalization;
using System.Text.RegularExpressions;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;
using SemanticPageSnapshot = HorseRacingPrediction.Scraping.Browser.Snapshots.PageSnapshot;

namespace HorseRacingPrediction.Scraping.Jra.Parsing;

public sealed class RaceOddsPageParser(TimeProvider? timeProvider = null) : IJraPageParser
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    public JraPageKind Kind => JraPageKind.RaceOdds;
    public int Priority => 95;
    public bool CanParse(SemanticPageSnapshot source) => JraSnapshotView.Create(source).Tables.Any(IsOddsTable);
    public IJraPage Parse(SemanticPageSnapshot source)
    {
        var snapshot = JraSnapshotView.Create(source);
        var table = snapshot.Tables.FirstOrDefault(IsOddsTable)
            ?? throw new JraPageParseException(Kind, snapshot.Url, "単勝オッズ表を取得できませんでした。");
        var text = $"{snapshot.Title} {string.Join(' ', snapshot.Headings)}";
        var date = Regex.Match(text, @"(?<y>\d{4})年\s*(?<m>\d{1,2})月\s*(?<d>\d{1,2})日");
        var number = Regex.Match(text, @"(?<n>\d{1,2})\s*(?:R|レース)");
        var course = RaceCourseNames.ParseAll(text).FirstOrDefault();
        if (!date.Success || !number.Success || course == RaceCourse.Unknown)
            throw new JraPageParseException(Kind, snapshot.Url, "対象レースを同定できませんでした。");
        var horseColumn = table.Headers.ToList().FindIndex(x => x.Contains("馬番"));
        var oddsColumn = table.Headers.ToList().FindIndex(x => x.Contains("単勝"));
        var popularityColumn = table.Headers.ToList().FindIndex(x => x.Contains("人気"));
        var entries = table.Rows.Select(row => ParseRow(row, horseColumn, oddsColumn, popularityColumn))
            .Where(x => x is not null).Cast<JraOddsEntry>().ToArray();
        if (entries.Length == 0) throw new JraPageParseException(Kind, snapshot.Url, "有効な単勝オッズがありません。");
        return new JraRaceOddsPage(snapshot.Url,
            new RaceId(new(int.Parse(date.Groups["y"].Value), int.Parse(date.Groups["m"].Value),
                int.Parse(date.Groups["d"].Value)), course, int.Parse(number.Groups["n"].Value)),
            _time.GetUtcNow(), entries);
    }
    private static bool IsOddsTable(JraTableView table) => table.Headers.Any(x => x.Contains("馬番"))
        && table.Headers.Any(x => x.Contains("単勝"));
    private static JraOddsEntry? ParseRow(IReadOnlyList<string> row, int horse, int odds, int popularity)
    {
        if (horse < 0 || odds < 0 || horse >= row.Count || odds >= row.Count
            || !int.TryParse(Regex.Match(row[horse], @"\d+").Value, out var number)
            || !decimal.TryParse(Regex.Match(row[odds], @"\d+(?:\.\d+)?").Value,
                NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) return null;
        int? rank = popularity >= 0 && popularity < row.Count
            && int.TryParse(Regex.Match(row[popularity], @"\d+").Value, out var parsed) ? parsed : null;
        return new(number, value, rank);
    }
}
