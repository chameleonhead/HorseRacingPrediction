namespace HorseRacingPrediction.Scraping.Jra.Pages;

/// <summary>
/// 想定外ページを例外にせず取得できるようにするためのフォールバックページ。
/// </summary>
public sealed record JraUnknownPage(
    string Url,
    string Title)
    : IJraPage
{
    public JraPageKind Kind =>
        JraPageKind.Unknown;
}
