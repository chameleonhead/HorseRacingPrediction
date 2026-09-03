namespace HorseRacingPrediction.Scraping.Jra.Navigation;

/// <summary>
/// JRAナビゲーションが失敗したことを表す例外。
/// </summary>
public sealed class JraNavigationException
    : Exception
{
    public JraNavigationException(
        string message)
        : base(message)
    {
    }

    public JraNavigationException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
