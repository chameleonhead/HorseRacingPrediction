using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Scraping.Jra.Parsing;

/// <summary>
/// PageSnapshotをIJraPageへ変換する責務。追加ブラウザー操作は行わない。
/// </summary>
public interface IJraPageParser
{
    JraPageKind Kind { get; }

    /// <summary>
    /// 複数のParserが一致した場合の優先度。高いほど優先される。
    /// </summary>
    int Priority { get; }

    bool CanParse(PageSnapshot snapshot);

    IJraPage Parse(PageSnapshot snapshot);
}
