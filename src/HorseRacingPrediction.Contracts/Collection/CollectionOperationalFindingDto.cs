namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionOperationalFindingDto(string Fingerprint, string Kind,
    CollectionFindingClassificationDto Classification, string Severity, DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt, string Summary, IReadOnlyList<string> Evidence,
    string? NextSafeOperation = null);
