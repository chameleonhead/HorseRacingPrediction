namespace HorseRacingPrediction.ApiClient;

/// <summary>Removes race-specific allowance marks from jockey master data.</summary>
public static class JockeyNameNormalizer
{
    private static readonly char[] AllowanceMarks = ['▲', '△', '☆', '★', '◇', '▽'];

    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var result = value.Trim();
        while (result.Length > 0 && AllowanceMarks.Contains(result[0]))
            result = result[1..].TrimStart();

        return result;
    }
}
