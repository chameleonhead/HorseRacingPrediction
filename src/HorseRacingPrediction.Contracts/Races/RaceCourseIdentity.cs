
namespace HorseRacingPrediction.Contracts.Races;

/// <summary>Shared JRA course identity; display spelling is not an identity boundary.</summary>
public static class RaceCourseIdentity
{
    private static readonly (string English, string Japanese)[] Courses =
    [
        ("Sapporo", "札幌"), ("Hakodate", "函館"), ("Fukushima", "福島"),
        ("Niigata", "新潟"), ("Tokyo", "東京"), ("Nakayama", "中山"),
        ("Chukyo", "中京"), ("Kyoto", "京都"), ("Hanshin", "阪神"), ("Kokura", "小倉")
    ];

    public static string? Canonicalize(string? value) => Find(value)?.Japanese;
    public static string? ResourceCode(string? value) => Find(value)?.English;
    private static (string English, string Japanese)? Find(string? value)
    {
        foreach (var course in Courses)
            if (string.Equals(value?.Trim(), course.English, StringComparison.OrdinalIgnoreCase)
                || value?.Trim() == course.Japanese) return course;
        return null;
    }
}
