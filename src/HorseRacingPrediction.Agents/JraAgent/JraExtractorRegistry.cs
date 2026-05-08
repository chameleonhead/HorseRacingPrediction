namespace HorseRacingPrediction.Agents.JraAgent;

/// <summary>
/// <see cref="JraPageKind"/> から対応する <see cref="IPageExtractor"/> を解決するレジストリ。
/// </summary>
public sealed class JraExtractorRegistry
{
    private readonly IReadOnlyDictionary<JraPageKind, IPageExtractor> _map;

    public JraExtractorRegistry(IEnumerable<IPageExtractor> extractors)
    {
        var map = new Dictionary<JraPageKind, IPageExtractor>();
        foreach (var extractor in extractors)
        {
            foreach (var kind in extractor.SupportedPageKinds)
                map[kind] = extractor;
        }
        _map = map;
    }

    /// <summary>指定ページ種別に対応する抽出器を返す。未登録の場合は null。</summary>
    public IPageExtractor? GetFor(JraPageKind kind)
        => _map.TryGetValue(kind, out var e) ? e : null;
}
