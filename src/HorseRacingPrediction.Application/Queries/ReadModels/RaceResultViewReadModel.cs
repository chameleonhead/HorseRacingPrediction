using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed record RaceResultViewReadModel(
    string RaceId,
    DateOnly RaceDate,
    string RacecourseCode,
    int RaceNumber,
    string RaceName,
    RaceStatus Status,
    string? WinningHorseName,
    string? WinningHorseId,
    DateTimeOffset? ResultDeclaredAt,
    string? StewardReportText,
    IReadOnlyList<EntryResultSnapshot> EntryResults,
    PayoutResultSnapshot? PayoutResult);
