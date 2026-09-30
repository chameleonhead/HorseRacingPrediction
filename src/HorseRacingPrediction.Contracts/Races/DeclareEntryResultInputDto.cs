namespace HorseRacingPrediction.Contracts.Races;

public sealed record DeclareEntryResultInputDto(
    int? FinishPosition,
    string? OfficialTime,
    string? MarginText,
    string? LastThreeFurlongTime,
    string? AbnormalResultCode,
    decimal? PrizeMoney,
    string? CornerPositions = null);
