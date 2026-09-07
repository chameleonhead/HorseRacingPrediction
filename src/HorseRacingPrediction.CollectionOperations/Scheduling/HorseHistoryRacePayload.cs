namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record HorseHistoryRacePayload(SubjectCollectionPayload Horse, DateOnly? RaceDate,
    string Course, string RaceName, string? LinkUrl, string? LinkTitle, string? ExclusionReason = null);
