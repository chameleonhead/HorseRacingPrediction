namespace HorseRacingPrediction.Scraping.Jra.Models;

/// <summary>
/// レース場の日本語表記からの変換。JRA表記変更時の影響範囲をここへ限定する。
/// </summary>
public static class RaceCourseNames
{
    // 検索順は判定優先度と一致させる。
    private static readonly (string Text, RaceCourse Course)[] Entries = Enum.GetValues<RaceCourse>()
        .Where(course => course != RaceCourse.Unknown)
        .Select(course => (HorseRacingPrediction.Contracts.Races.RaceCourseIdentity.Canonicalize(course.ToString())
            ?? throw new InvalidOperationException($"Unmapped course: {course}"), course)).ToArray();

    /// <summary>
    /// <see cref="RaceCourse"/> から、JRAサイト上の日本語表記へ変換する。
    /// <see cref="RaceCourse.Unknown"/> は永続化・遷移に使えないため例外にする。
    /// </summary>
    public static string GetJraName(RaceCourse course)
    {
        foreach (var (name, candidate) in Entries)
        {
            if (candidate == course)
            {
                return name;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(course),
            course,
            "対応するJRA表記がありません。");
    }

    public static RaceCourse Parse(string text)
    {
        foreach (var (name, course) in Entries)
        {
            if (text.Contains(name, StringComparison.Ordinal))
            {
                return course;
            }
        }

        return RaceCourse.Unknown;
    }

    /// <summary>
    /// テキスト内に出現する競馬場名を、出現順にすべて抽出する。
    /// </summary>
    public static IReadOnlyList<RaceCourse> ParseAll(string text)
    {
        return Entries
            .Select(entry => (entry.Course, Index: text.IndexOf(entry.Text, StringComparison.Ordinal)))
            .Where(x => x.Index >= 0)
            .OrderBy(x => x.Index)
            .Select(x => x.Course)
            .ToArray();
    }
}
