namespace HorseRacingPrediction.Scraping.Jra;

/// <summary>
/// <see cref="JraSession"/> の生成を担う。生成されたセッションはBrowserの所有権を
/// 内包しているため、呼び出し側は <c>await using</c> でセッションを破棄すればよい。
/// </summary>
public interface IJraSessionFactory
{
    Task<JraSession> CreateAsync(CancellationToken cancellationToken = default);
}
