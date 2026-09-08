namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record RaceReacquisitionPayload(string RaceId, DateOnly RaceDate, string Racecourse, int RaceNumber);
