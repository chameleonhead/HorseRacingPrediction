namespace HorseRacingPrediction.Api.Web.Components.Shared;

/// <summary>
/// Playwrightのブラウザログ等、冗長な補足情報が本文に連結されたエラーメッセージを
/// 「要約」と「補足情報」に分割する。
/// </summary>
public static class ErrorMessageSplitter
{
    private static readonly string[] SupplementaryMarkers =
    [
        "Browser logs:",
        "Call log:",
        "=========================== logs ===========================",
    ];

    public static (string Summary, string? Supplementary) Split(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return (string.Empty, null);
        }

        var trimmed = message.Trim();
        var earliestIndex = -1;
        foreach (var marker in SupplementaryMarkers)
        {
            var index = trimmed.IndexOf(marker, StringComparison.Ordinal);
            if (index > 0 && (earliestIndex == -1 || index < earliestIndex))
            {
                earliestIndex = index;
            }
        }

        if (earliestIndex <= 0)
        {
            return (trimmed, null);
        }

        var summary = trimmed[..earliestIndex].TrimEnd();
        var supplementary = trimmed[earliestIndex..].Trim();
        return (summary, supplementary.Length > 0 ? supplementary : null);
    }
}
