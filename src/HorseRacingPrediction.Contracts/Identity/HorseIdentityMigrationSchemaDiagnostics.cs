using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Identity;

[JsonConverter(typeof(JsonStringEnumConverter<HorseIdentityMigrationSchemaSurface>))]
public enum HorseIdentityMigrationSchemaSurface
{
    EventStore,
    CollectionPlatform,
    PredictionSchedule,
    LocalCollectionQueue,
}

[JsonConverter(typeof(JsonStringEnumConverter<HorseIdentityMigrationSchemaStatus>))]
public enum HorseIdentityMigrationSchemaStatus
{
    Observed,
    NotConfigured,
    Unavailable,
    UnknownShape,
    Unsupported,
    Incomplete,
}

[JsonConverter(typeof(JsonStringEnumConverter<HorseIdentityMigrationSchemaDifferenceKind>))]
public enum HorseIdentityMigrationSchemaDifferenceKind
{
    MigrationHistory,
    MissingTable,
    ColumnShape,
    IndexShape,
    DefaultMismatch,
    ForeignKeyMismatch,
    SqlShapeUnsupported,
    UnknownObject,
    IncompleteScan,
    StoreUnavailable,
}

[JsonConverter(typeof(JsonStringEnumConverter<HorseIdentityMigrationSchemaObjectKind>))]
public enum HorseIdentityMigrationSchemaObjectKind
{
    Table,
    Index,
    View,
    Trigger,
    Other,
}

[JsonConverter(typeof(JsonStringEnumConverter<HorseIdentityMigrationSchemaDeclaredType>))]
public enum HorseIdentityMigrationSchemaDeclaredType
{
    Integer,
    Text,
    Real,
    Blob,
    Numeric,
    Other,
}

/// <summary>Bounded, read-only schema evidence. It deliberately excludes paths, SQL, and row data.</summary>
public sealed record HorseIdentityMigrationSchemaDiagnostics(
    DateTimeOffset ObservedAtUtc,
    bool IsComplete,
    IReadOnlyList<HorseIdentityMigrationSchemaSurfaceDiagnostic> Surfaces)
{
    public const int MaximumReportBytes = 1024 * 1024;
    public const int MaximumIdentifierBytes = 512;
}

public sealed record HorseIdentityMigrationSchemaSurfaceDiagnostic(
    HorseIdentityMigrationSchemaSurface Surface,
    HorseIdentityMigrationSchemaStatus Status,
    int TableCount,
    int UnknownObjectCount,
    string? Fingerprint,
    IReadOnlyList<int> MigrationHistoryVersions,
    IReadOnlyList<HorseIdentityMigrationSchemaDifference> Differences);

public sealed record HorseIdentityMigrationSchemaDifference(
    HorseIdentityMigrationSchemaDifferenceKind Kind,
    HorseIdentityMigrationSchemaObjectKind ObjectKind,
    string? KnownIdentifier = null,
    HorseIdentityMigrationSchemaShapeDescriptor? Expected = null,
    HorseIdentityMigrationSchemaShapeDescriptor? Observed = null);

/// <summary>Closed, data-free schema facets. Fingerprints are lowercase SHA-256 hex only.</summary>
public sealed record HorseIdentityMigrationSchemaShapeDescriptor(
    HorseIdentityMigrationSchemaDeclaredType? DeclaredType = null,
    bool? Nullable = null,
    int? PrimaryKeyOrdinal = null,
    int? ObjectCount = null,
    bool? Unique = null,
    bool? Partial = null,
    string? Fingerprint = null);
