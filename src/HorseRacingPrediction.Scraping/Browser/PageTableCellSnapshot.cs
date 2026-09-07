namespace HorseRacingPrediction.Scraping.Browser;

/// <summary>
/// テーブルセルの表示文字列と、class付き子孫要素の汎用DOM情報。
/// classの意味はprovider parserが解釈し、ブラウザー層では解釈しない。
/// </summary>
public sealed record PageTableCellSnapshot(
    string Text,
    IReadOnlyList<PageDomTextFragment> Fragments)
{
    public PageDomTextFragment? FindByClass(string classToken)
        => Fragments.FirstOrDefault(fragment =>
            fragment.ClassTokens.Contains(classToken, StringComparer.Ordinal));
}

public sealed record PageDomTextFragment(
    string TagName,
    IReadOnlyList<string> ClassTokens,
    string Text,
    string? Href = null);
