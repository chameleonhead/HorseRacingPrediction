namespace HorseRacingPrediction.Scraping.Jra.Pages;

/// <summary>
/// JRAページの解析に失敗したことを表す例外。DOMやページ内容全文は含めない。
/// </summary>
public sealed class JraPageParseException
    : Exception
{
    public JraPageParseException(
        JraPageKind pageKind,
        string url,
        string message)
        : base(
            $"JRAページ解析に失敗しました。 " +
            $"Kind={pageKind}, Url={url}, Reason={message}")
    {
        PageKind = pageKind;
        Url = url;
    }

    public JraPageKind PageKind { get; }

    public string Url { get; }
}
