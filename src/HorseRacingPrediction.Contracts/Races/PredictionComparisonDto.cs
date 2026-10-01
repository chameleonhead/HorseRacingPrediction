namespace HorseRacingPrediction.Contracts.Races;

public sealed record PredictionComparisonDto(string RaceId, string? RaceName, string? WinningHorseName,
    DateTimeOffset? ResultDeclaredAt, IReadOnlyList<PredictionComparisonTicketDto> PredictionTickets,
    IReadOnlyList<EntryResultSnapshotDto> EntryResults, RacePayoutResultDto? PayoutResult);
