namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed record EntryResultSnapshot(
    string EntryId,
    string HorseId,
    int HorseNumber,
    int? FinishPosition,
    string? OfficialTime,
    string? MarginText,
    string? LastThreeFurlongTime,
    string? AbnormalResultCode,
    decimal? PrizeMoney,
    string? CornerPositions,
    int? Popularity = null, int? OriginalFinishPosition = null, bool IsDeadHeat = false, decimal? Average1F = null);
