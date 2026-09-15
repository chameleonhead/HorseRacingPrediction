namespace HorseRacingPrediction.Scraping.Browser.Diagnostics;

/// <summary>
/// Source-backed estimates for the current locator-loop implementation. These are protocol-call
/// counts, not elapsed-time predictions, and deliberately exclude page-readiness waits and clicks.
/// </summary>
public static class BrowserProtocolBaseline
{
    public static int LinkExtractionHappyPath(int anchorCount) =>
        1 + 7 * NonNegative(anchorCount);

    public static int LinkExtractionWorstTextFallback(int anchorCount) =>
        1 + 13 * NonNegative(anchorCount);

    public static int ClickLinkHappyPath(int targetZeroBasedIndex) =>
        3 * (NonNegative(targetZeroBasedIndex) + 1) + 4;

    public static int ClickableDiscoveryHappyPath(int candidateCount, int matchingCandidateCount)
    {
        candidateCount = NonNegative(candidateCount);
        matchingCandidateCount = Math.Clamp(matchingCandidateCount, 0, candidateCount);
        return 1 + 3 * candidateCount + 2 * matchingCandidateCount;
    }

    public static int FormExtractionHappyPath(int formCount, int fieldCount,
        int selectCount = 0, int totalSelectOptionCount = 0)
    {
        formCount = NonNegative(formCount);
        fieldCount = NonNegative(fieldCount);
        selectCount = Math.Clamp(selectCount, 0, fieldCount);
        totalSelectOptionCount = NonNegative(totalSelectOptionCount);
        return 1 + 6 * formCount + 12 * fieldCount + selectCount + totalSelectOptionCount;
    }

    private static int NonNegative(int value) =>
        value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
}
