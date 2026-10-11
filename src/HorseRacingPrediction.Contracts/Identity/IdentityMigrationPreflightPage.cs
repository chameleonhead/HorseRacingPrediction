using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Identity;

[JsonConverter(typeof(JsonStringEnumConverter<IdentityMigrationPreflightPageState>))]
public enum IdentityMigrationPreflightPageState
{
    InProgress,
    Complete,
    Incomplete,
}

[JsonConverter(typeof(JsonStringEnumConverter<IdentityMigrationPreflightSurface>))]
public enum IdentityMigrationPreflightSurface
{
    EventStore,
    CollectionPlatform,
    PredictionSchedule,
    LocalCollectionQueue,
}

[JsonConverter(typeof(JsonStringEnumConverter<IdentityMigrationPreflightPhase>))]
public enum IdentityMigrationPreflightPhase
{
    HorseRoots,
    HorseEvents,
    JraProfiles,
    HorseRaceHistories,
    HorseWeightHistories,
    CollectionResources,
}

[JsonConverter(typeof(JsonStringEnumConverter<IdentityMigrationPreflightObservationStatus>))]
public enum IdentityMigrationPreflightObservationStatus
{
    Observed,
    SchemaOnly,
    Unsupported,
    UnknownShape,
    Truncated,
    Unavailable,
    NotConfigured,
    NotInspected,
    Unverifiable,
    Conflict,
}

public sealed record IdentityMigrationPreflightCounts(
    int Observed,
    int SchemaOnly,
    int Unsupported,
    int UnknownShape,
    int Truncated,
    int Unavailable,
    int NotConfigured,
    int NotInspected,
    int Unverifiable,
    int Conflict);

public sealed record IdentityMigrationPreflightSurfaceObservation(
    IdentityMigrationPreflightSurface Surface,
    IdentityMigrationPreflightObservationStatus Status,
    DateTimeOffset? ObservedAtUtc,
    int KnownObjectCount,
    int UnknownObjectCount);

public sealed record IdentityMigrationPreflightRaceReference(
    string RaceId,
    int HorseNumber);

public sealed record IdentityMigrationPreflightHorseEvidence(
    string HorseId,
    IdentityMigrationPreflightPhase Phase,
    IdentityMigrationPreflightObservationStatus Status,
    string? CurrentRegisteredName,
    string? CurrentNormalizedName,
    DateOnly? CurrentBirthDate,
    string? InitialRegisteredName,
    string? InitialNormalizedName,
    DateOnly? InitialBirthDate,
    string? CanonicalJraIdentity,
    IdentityMigrationPreflightObservationStatus JraIdentityStatus,
    IReadOnlyList<IdentityMigrationPreflightRaceReference> RelatedRaces,
    int RelatedRaceCount,
    bool RelatedRacesTruncated);

public sealed record IdentityMigrationPreflightPage(
    IdentityMigrationPreflightPageState State,
    IdentityMigrationPreflightPhase Phase,
    DateTimeOffset? ObservedAtUtc,
    int RowsInspected,
    IdentityMigrationPreflightCounts Counts,
    IReadOnlyList<IdentityMigrationPreflightSurfaceObservation> Surfaces,
    IReadOnlyList<IdentityMigrationPreflightHorseEvidence> Evidence,
    string? NextCursor,
    bool IsNonAtomic = true);
