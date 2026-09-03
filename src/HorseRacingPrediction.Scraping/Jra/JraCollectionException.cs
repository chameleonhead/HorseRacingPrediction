namespace HorseRacingPrediction.Scraping.Jra;

/// <summary>
/// JRAサイトからのデータ収集（Workflow層）が失敗したことを表す共通例外。
/// Navigation/Parsing層固有の例外（<see cref="Navigation.JraNavigationException"/>、
/// <see cref="Pages.JraPageParseException"/>）とは責務が異なり、Workflow層が
/// 「想定外ページだった」等の収集失敗を呼び出し側へ伝える際に使う。
/// </summary>
public sealed class JraCollectionException
    : Exception
{
    public JraCollectionException(
        string message)
        : base(message)
    {
    }

    public JraCollectionException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
