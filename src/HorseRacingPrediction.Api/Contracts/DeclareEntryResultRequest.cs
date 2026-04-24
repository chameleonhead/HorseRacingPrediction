namespace HorseRacingPrediction.Api.Contracts;

public sealed record DeclareEntryResultRequest(
    int? FinishPosition,
    string? OfficialTime,
    string? MarginText,
    string? LastThreeFurlongTime,
    string? AbnormalResultCode,
    decimal? PrizeMoney,
    string? CornerPositions = null);
