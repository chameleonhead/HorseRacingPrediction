using HorseRacingPrediction.Scraping.Browser.Snapshots;

namespace HorseRacingPrediction.Scraping.Tests.TestSupport;

internal static class SemanticSnapshotFactory
{
    public static PageSnapshot Create(
        string url = "https://example.test/",
        string? title = null,
        PageContentNode? root = null,
        IReadOnlyList<PageTableSnapshot>? tables = null,
        IReadOnlyList<PageLinkSnapshot>? links = null)
        => new()
        {
            Url = Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed : new Uri("about:blank"),
            Title = title,
            Root = root ?? new PageContentNode { Kind = PageContentKind.Document },
            Metadata = new PageMetadataSnapshot { Meta = new Dictionary<string, string>(), JsonLd = [] },
            KeyValues = [],
            Tables = tables ?? [],
            Links = links ?? [],
            Images = [],
            Forms = [],
            Diagnostics = [],
        };
}
