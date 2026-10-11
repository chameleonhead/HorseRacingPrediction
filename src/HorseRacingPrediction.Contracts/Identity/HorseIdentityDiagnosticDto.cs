namespace HorseRacingPrediction.Contracts.Identity;

public sealed record HorseIdentityDiagnosticDto(
    string HorseId,
    DateTimeOffset EventStoreObservedFromUtc,
    DateTimeOffset EventStoreObservedToUtc,
    DateTimeOffset CollectionStoreObservedFromUtc,
    DateTimeOffset CollectionStoreObservedToUtc,
    string CurrentRecordStatus,
    string? CurrentRegisteredName,
    string? CurrentNormalizedName,
    DateOnly? CurrentBirthDate,
    string RegistrationStatus,
    string? InitialRegisteredName,
    string? InitialNormalizedName,
    DateOnly? InitialBirthDate,
    IReadOnlyList<HorseSourceEvidenceDto> Sources,
    IReadOnlyList<HorseIdentityComparisonDto> Comparisons,
    IReadOnlyList<string> EvidenceBlockers,
    bool SourcesTruncated,
    string Coverage);
