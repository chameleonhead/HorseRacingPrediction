namespace HorseRacingPrediction.AgentClient.Web.ApiBrowsing;

public sealed record RaceEntryResultDto(
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
    string? CornerPositions);
