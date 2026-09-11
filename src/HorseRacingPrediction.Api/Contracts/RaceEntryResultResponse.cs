namespace HorseRacingPrediction.Api.Contracts;

public sealed record RaceEntryResultResponse(
    string EntryId,
    string HorseId,
    string? HorseName,
    int HorseNumber,
    int? FinishPosition,
    string? OfficialTime,
    string? MarginText,
    string? LastThreeFurlongTime,
    string? AbnormalResultCode,
    decimal? PrizeMoney,
    string? CornerPositions,
    int? Popularity = null, int? OriginalFinishPosition = null, bool IsDeadHeat = false, decimal? Average1F = null,
    decimal? AdditionalPrizeMoney = null);
