using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Identity;

/// <summary>Requests a journal-only admission after the displayed preview is freshly revalidated.</summary>
public sealed record HorseIdentityMigrationStageRequest(
    HorseIdentityMigrationManifest? Manifest,
    string? PreviewFingerprint)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HorseIdentityMigrationAdmissionKind? RequestKind { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? RequestKey { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<HorseIdentityMigrationAdmissionKind>))]
public enum HorseIdentityMigrationAdmissionKind
{
    BoundV1,
    PendingV2,
}

/// <summary>Safe, bounded public view of a staged or previously executed migration.</summary>
public sealed record HorseIdentityMigrationOperationResponse(
    Guid OperationId,
    string Status,
    string Reason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<HorseIdentityMigrationOperationResult> Results)
{
    /// <summary>The immutable request saved with this operation, when it was staged for supervised execution.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HorseIdentityMigrationManifest? SavedManifest { get; init; }

    /// <summary>The source preview fingerprint captured when the request was staged.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PreviewFingerprint { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HorseIdentityMigrationAdmissionKind? RequestKind { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExecutionPhase { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ExecutionUpdatedAt { get; init; }
}

public sealed record HorseIdentityMigrationOperationResult(
    string Code,
    string? Surface,
    int Count);

public sealed record HorseIdentityMigrationOperationError(string Code);
