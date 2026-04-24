namespace HorseRacingPrediction.Domain.Races;

public sealed record EntryResultDetails(
    string EntryId,
    int? FinishPosition,
    string? OfficialTime,
    string? MarginText,
    string? LastThreeFurlongTime,
    string? AbnormalResultCode,
    decimal? PrizeMoney,
    string? CornerPositions);
