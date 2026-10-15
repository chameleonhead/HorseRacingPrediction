namespace HorseRacingPrediction.Contracts.Identity;

/// <summary>Requests a journal-only admission after the displayed preview is freshly revalidated.</summary>
public sealed record HorseIdentityMigrationStageRequest(
    HorseIdentityMigrationManifest? Manifest,
    string? PreviewFingerprint);

/// <summary>Safe, bounded public view of a staged or previously executed migration.</summary>
public sealed record HorseIdentityMigrationOperationResponse(
    Guid OperationId,
    string Status,
    string Reason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<HorseIdentityMigrationOperationResult> Results);

public sealed record HorseIdentityMigrationOperationResult(
    string Code,
    string? Surface,
    int Count);

public sealed record HorseIdentityMigrationOperationError(string Code);
