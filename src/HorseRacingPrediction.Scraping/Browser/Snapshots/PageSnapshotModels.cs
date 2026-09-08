using System.Text.Json;

namespace HorseRacingPrediction.Scraping.Browser.Snapshots;

public sealed record PageSnapshot
{
    public required Uri Url { get; init; }
    public string? Title { get; init; }
    public required PageContentNode Root { get; init; }
    public required PageMetadataSnapshot Metadata { get; init; }
    public required IReadOnlyList<PageKeyValueSnapshot> KeyValues { get; init; }
    public required IReadOnlyList<PageTableSnapshot> Tables { get; init; }
    public required IReadOnlyList<PageLinkSnapshot> Links { get; init; }
    public required IReadOnlyList<PageImageSnapshot> Images { get; init; }
    public required IReadOnlyList<PageFormSnapshot> Forms { get; init; }
    public required IReadOnlyList<PageSnapshotDiagnostic> Diagnostics { get; init; }
}

public sealed record PageContentNode
{
    public required PageContentKind Kind { get; init; }
    public string? Text { get; init; }
    public int? HeadingLevel { get; init; }
    public string? Role { get; init; }
    public string? AccessibleName { get; init; }
    public PageElementLocation? Location { get; init; }
    public PageSourceReference? Source { get; init; }
    public IReadOnlyList<PageContentNode> Children { get; init; } = [];
}

public enum PageContentKind
{
    Document,
    Section,
    Navigation,
    Article,
    Aside,
    Heading,
    Paragraph,
    Text,
    List,
    ListItem,
    DefinitionList,
    Link,
    Image,
    Quote,
    Code,
    Button,
}

public sealed record PageElementLocation(double X, double Y, double Width, double Height);

public sealed record PageSourceReference(string? TagName, string? ElementId, string? LocatorHint);

public sealed record PageMetadataSnapshot
{
    public string? Description { get; init; }
    public Uri? CanonicalUrl { get; init; }
    public string? Language { get; init; }
    public required IReadOnlyDictionary<string, string> Meta { get; init; }
    public required IReadOnlyList<JsonLdSnapshot> JsonLd { get; init; }
}

public sealed record JsonLdSnapshot
{
    public required JsonElement Value { get; init; }
}

public sealed record PageKeyValueSnapshot(string Key, string Value, PageSourceReference? Source);

public sealed record PageTableSnapshot
{
    public string? Caption { get; init; }
    public required IReadOnlyList<PageTableRowSnapshot> Rows { get; init; }
    public PageSourceReference? Source { get; init; }
}

public sealed record PageTableRowSnapshot
{
    public required IReadOnlyList<PageTableCellSnapshot> Cells { get; init; }
}

public sealed record PageTableCellSnapshot
{
    public required string Text { get; init; }
    public bool IsHeader { get; init; }
    public int RowSpan { get; init; } = 1;
    public int ColumnSpan { get; init; } = 1;
    public PageSourceReference? Source { get; init; }
    public IReadOnlyList<PageElementFragmentSnapshot> Fragments { get; init; } = [];
}

public sealed record PageElementFragmentSnapshot
{
    public required string TagName { get; init; }
    public string? Text { get; init; }
    public IReadOnlyList<string> ClassTokens { get; init; } = [];
    public Uri? Url { get; init; }
    public string? RawUrl { get; init; }
    public string? Role { get; init; }
    public string? AccessibleName { get; init; }
    public PageSourceReference? Source { get; init; }
}

public sealed record PageLinkSnapshot
{
    public required string Text { get; init; }
    public Uri? Url { get; init; }
    public string? RawHref { get; init; }
    public string? Relation { get; init; }
    public string? Title { get; init; }
    public string? AccessibleName { get; init; }
    public PageSourceReference? Source { get; init; }
}

public sealed record PageImageSnapshot
{
    public Uri? Source { get; init; }
    public string? RawSource { get; init; }
    public string? AltText { get; init; }
    public string? Title { get; init; }
    public string? AccessibleName { get; init; }
    public PageSourceReference? SourceReference { get; init; }
}

public sealed record PageFormSnapshot
{
    public string? Name { get; init; }
    public Uri? Action { get; init; }
    public string? RawAction { get; init; }
    public string? Method { get; init; }
    public required IReadOnlyList<PageFormControlSnapshot> Controls { get; init; }
    public PageSourceReference? Source { get; init; }
}

public sealed record PageFormControlSnapshot
{
    public string? Name { get; init; }
    public string? Type { get; init; }
    public string? Label { get; init; }
    public string? Value { get; init; }
    public string? Placeholder { get; init; }
    public string? AccessibleName { get; init; }
    public IReadOnlyList<string> SelectedOptions { get; init; } = [];
    public bool Required { get; init; }
    public bool Disabled { get; init; }
    public PageSourceReference? Source { get; init; }
}

public sealed record PageSnapshotDiagnostic(
    PageSnapshotDiagnosticSeverity Severity,
    string Code,
    string Message,
    PageSourceReference? Source = null);

public enum PageSnapshotDiagnosticSeverity
{
    Information,
    Warning,
}

public sealed record PageSnapshotOptions
{
    public bool IncludeHiddenContent { get; init; }
    public bool IncludeMetadata { get; init; } = true;
    public bool IncludeStructuredData { get; init; } = true;
    public bool IncludeLinks { get; init; } = true;
    public bool IncludeForms { get; init; } = true;
    public bool IncludeImages { get; init; } = true;
    public bool IncludeElementLocations { get; init; }
    public bool NormalizeWhitespace { get; init; } = true;
    public PageSnapshotPruningLevel Pruning { get; init; } = PageSnapshotPruningLevel.Safe;
}

public enum PageSnapshotPruningLevel
{
    /// <summary>Retains otherwise transparent wrapper grouping, except executable and style noise.</summary>
    None,

    /// <summary>Removes hidden/empty content and flattens meaningless wrappers without removing landmarks.</summary>
    Safe,

    /// <summary>
    /// Applies narrow boilerplate heuristics in addition to safe pruning. This mode is intentionally lossy and can
    /// remove information that a scraper needs.
    /// </summary>
    Aggressive,
}
