using HorseRacingPrediction.Scraping.Browser;

namespace HorseRacingPrediction.Scraping.JraNavigation;

/// <summary>
/// 特定の <see cref="JraPageKind"/> に対応するデータ抽出器のインターフェース。
/// </summary>
public interface IPageExtractor
{
    /// <summary>この抽出器が対応するページ種別の一覧。</summary>
    JraPageKind[] SupportedPageKinds { get; }

    /// <summary>
    /// 現在ページからデータを抽出する。
    /// ページ遷移は行わない（読み取り専用操作）。
    /// </summary>
    Task<object?> ExtractAsync(IWebBrowser browser, CancellationToken cancellationToken = default);
}
