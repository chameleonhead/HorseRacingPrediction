using System.Globalization;
using System.Text.RegularExpressions;
using HorseRacingPrediction.Scraping.Browser.Snapshots;
using HorseRacingPrediction.Scraping.Jra.Models;

namespace HorseRacingPrediction.Scraping.Jra.Parsing;

/// <summary>Conservative reader for the official dated programme, never for generic page text.</summary>
public static class JraMeetingCancellationParser
{
    public static Uri ProgrammeUrl(DateOnly date)
        => new(FormattableString.Invariant($"https://www.jra.go.jp/keiba/calendar{date.Year}/{date.Year}/{date.Month}/{date:MMdd}.html"));

    public static JraMeetingCancellation? Parse(PageSnapshot snapshot, DateOnly date, RaceCourse course)
    {
        if (course == RaceCourse.Unknown || snapshot.Url.Scheme != "https"
            || snapshot.Url.Host is not ("jra.jp" or "www.jra.jp" or "jra.go.jp" or "www.jra.go.jp")
            || snapshot.Url.AbsolutePath != ProgrammeUrl(date).AbsolutePath)
            return null;

        var headings = snapshot.FindHeadings().Where(x => x.HeadingLevel == 1)
            .Select(x => Normalize(x.GetEffectiveText()))
            .Where(x => x.EndsWith("競馬番組", StringComparison.Ordinal)).ToArray();
        var datePrefix = FormattableString.Invariant($"{date.Year}年{date.Month}月{date.Day}日");
        if (headings.Length != 1 || !headings[0].StartsWith(datePrefix, StringComparison.Ordinal)
            || !headings[0].EndsWith("競馬番組", StringComparison.Ordinal))
            return null;

        var courseName = RaceCourseNames.GetJraName(course);
        var captionPattern = $"^(?<meeting>[1-9][0-9]*)回{Regex.Escape(courseName)}(?<day>[1-9][0-9]*)日$";
        var candidates = snapshot.Tables.Select(table => (Table: table,
            Match: Regex.Match(Normalize(table.Caption), captionPattern)))
            .Where(x => x.Match.Success).ToArray();
        // Duplicate/ambiguous meeting identities must not be silently accepted.
        if (candidates.Length != 1) return null;
        var candidate = candidates[0];
        // A whole-meeting cancellation replaces the race rows with one colspan notice.
        // Normal race rows, a single cancelled race, or a general disclaimer are not evidence.
        if (candidate.Table.Rows.Count != 1 || candidate.Table.Rows[0].Cells.Count != 1)
            return null;
        var notice = Normalize(candidate.Table.Rows[0].Cells[0].Text);
        if (!Regex.IsMatch(notice, $"^{Regex.Escape(courseName)}競馬は(?:台風|降雪|積雪|悪天候)のため中止[。．]"))
            return null;
        if (!int.TryParse(candidate.Match.Groups["meeting"].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var meeting)
            || !int.TryParse(candidate.Match.Groups["day"].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var day))
            return null;
        return new(date, course, meeting, day, snapshot.Url);
    }

    private static string Normalize(string? value)
        => Regex.Replace(value ?? string.Empty, @"\s+", string.Empty);
}
