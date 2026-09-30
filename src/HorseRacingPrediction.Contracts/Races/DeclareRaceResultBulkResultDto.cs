namespace HorseRacingPrediction.Contracts.Races;

/// <summary>One successful bulk operation's summary and per-item outcomes.</summary>
public sealed record DeclareRaceResultBulkResultDto(
    string RaceId,
    IReadOnlyList<string> Errors,
    IReadOnlyList<RaceResultBulkItemOutcomeDto>? Outcomes = null,
    bool CorePersisted = false,
    IReadOnlyList<string>? RelatedErrors = null);
