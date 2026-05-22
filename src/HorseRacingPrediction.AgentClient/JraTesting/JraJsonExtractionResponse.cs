using HorseRacingPrediction.Agents.Browser;

namespace HorseRacingPrediction.AgentClient.JraTesting;

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