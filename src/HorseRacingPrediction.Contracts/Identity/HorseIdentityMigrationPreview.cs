using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Identity;

/// <summary>Safe, read-only readiness summary for a horse identity migration admission preview.</summary>
public sealed record HorseIdentityMigrationPreview(
    bool CanStage,
    string? Fingerprint,
    IReadOnlyList<HorseIdentityMigrationPreviewBlocker> Blockers,
    IReadOnlyList<HorseIdentityMigrationPreviewMapping> Mappings,
    IReadOnlyList<HorseIdentityMigrationPreviewStore> Stores,
    IReadOnlyList<HorseIdentityMigrationPreviewRoot> Roots,
    IReadOnlyList<HorseIdentityMigrationPreviewPage> Pages,
    long InspectedRows,
    long InspectedBytes)
{
    public const int MaximumSummaryBytes = 1024 * 1024;

    public HorseIdentityMigrationSchemaDiagnostics? SchemaDiagnostics { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HorseIdentityMigrationInspectionDiagnostic? InspectionDiagnostic { get; init; }
}

/// <summary>Bounded point-read review of only the selected migration roots; it is not full-store evidence.</summary>
public sealed record HorseIdentityMigrationTargetReview(
    bool CanSavePending,
    IReadOnlyList<HorseIdentityMigrationTargetReviewBlocker> Blockers,
    IReadOnlyList<HorseIdentityMigrationPreviewMapping> Mappings,
    IReadOnlyList<HorseIdentityMigrationTargetReviewRoot> Roots);

[JsonConverter(typeof(JsonStringEnumConverter<HorseIdentityMigrationTargetReviewBlocker>))]
public enum HorseIdentityMigrationTargetReviewBlocker
{
    InvalidManifest,
    ReadUnavailable,
    SourceRootMissing,
    DuplicateSource,
    DestinationCollision,
}

[JsonConverter(typeof(JsonStringEnumConverter<HorseIdentityMigrationTargetReviewRootStatus>))]
public enum HorseIdentityMigrationTargetReviewRootStatus
{
    Present,
    Missing,
    Duplicate,
    DestinationCollision,
    Unavailable,
}

public sealed record HorseIdentityMigrationTargetReviewRoot(
    string OldHorseId,
    string DestinationHorseId,
    HorseIdentityMigrationTargetReviewRootStatus Status);

[JsonConverter(typeof(JsonStringEnumConverter<HorseIdentityMigrationInspectionPhase>))]
public enum HorseIdentityMigrationInspectionPhase
{
    Request,
    Schema,
    Roots,
    TypedReferences,
    Complete,
}

[JsonConverter(typeof(JsonStringEnumConverter<HorseIdentityMigrationInspectionResult>))]
public enum HorseIdentityMigrationInspectionResult
{
    Running,
    Completed,
    Incomplete,
    DeadlineExceeded,
}

/// <summary>Optional, bounded progress metadata. Counters describe completed pages only.</summary>
public sealed record HorseIdentityMigrationInspectionDiagnostic(
    HorseIdentityMigrationInspectionPhase Phase,
    HorseIdentityMigrationInspectionResult Result,
    HorseIdentityMigrationSchemaSurface? LastCompletedSurface,
    string? LastCompletedTable,
    long InspectedRowsLowerBound,
    long InspectedBytesLowerBound,
    long ElapsedMilliseconds);

public enum HorseIdentityMigrationPreviewBlocker
{
    InvalidManifest,
    StoreUnavailable,
    DestinationCollision,
    IncompleteEvidence,
    ReadUnavailable,
    SummaryTooLarge,
}

public sealed record HorseIdentityMigrationPreviewMapping(
    string OldHorseId,
    string JraIdentity,
    string DestinationHorseId);

public sealed record HorseIdentityMigrationPreviewStore(
    string Surface,
    string Status,
    int TableCount,
    int UnknownObjectCount,
    string? SchemaFingerprint);

public sealed record HorseIdentityMigrationPreviewRoot(
    string OldHorseId,
    string DestinationHorseId,
    string Status,
    bool HasTargetCollision);

public sealed record HorseIdentityMigrationPreviewPage(
    string Surface,
    string Table,
    long RowCount,
    string BeforeHash,
    string ExpectedAfterHash);
