using HorseRacingPrediction.Scraping.Browser;

namespace HorseRacingPrediction.Collector.JraTesting;

public sealed record JraJsonExtractionResponse(
    string InputUrl,
    string ResolvedUrl,
    string PageKind,
    string ExtractionMode,
    string? Title,
    IReadOnlyList<string> Headings,
    int TableCount,
    int LinkCount,
    object? Data,
    PageSnapshot? Snapshot,
    string? Error = null);