namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record SubjectCollectionPayload(string SubjectId, string SubjectType, string Name,
    DateOnly? BirthDate = null, string? SourceIdentity = null);
