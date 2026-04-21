namespace HorseRacingPrediction.Agents.Scrapers;

/// <summary>
/// サイト固有のスクレイパーインターフェース。
/// AIエージェントがページURLを発見した後、そのURLから構造化データを抽出する責務を持つ。
/// </summary>
/// <typeparam name="TResult">抽出するデータの型</typeparam>
public interface IScraper<TResult>
{
    /// <summary>
    /// 指定した URL のページからデータを抽出する。
    /// </summary>
    /// <param name="url">抽出対象の URL</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>抽出したデータ。取得できなかった場合は <c>null</c></returns>
    Task<TResult?> ScrapeAsync(string url, CancellationToken cancellationToken = default);
}
