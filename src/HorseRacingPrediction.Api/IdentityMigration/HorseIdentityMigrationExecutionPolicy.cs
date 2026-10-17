using System.Security.Cryptography;
using System.Text.Json;
using HorseRacingPrediction.Contracts.Identity;

namespace HorseRacingPrediction.Api.IdentityMigration;

internal enum HorseIdentityMigrationExecutionPhase
{
    IntentRecorded,
    Preparing,
    Draining,
    StoppingApi,
    ApiExited,
    BackedUp,
    Applying,
    DataSucceeded,
    Verifying,
    Verified,
    Failed,
}

internal sealed record HorseIdentityMigrationExecutionIntent(
    Guid OperationId,
    string ImmutableRequestHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    HorseIdentityMigrationExecutionPhase Phase);

/// <summary>Pure admission, request identity, and phase policy for the future supervised executor.</summary>
internal static class HorseIdentityMigrationExecutionPolicy
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static string ComputeImmutableRequestHash(
        HorseIdentityMigrationManifest? manifest,
        string previewFingerprint)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(previewFingerprint);

        var canonicalEntries = (manifest.Entries ?? Array.Empty<HorseIdentityMigrationManifestEntry?>())
            .Select(entry => entry is null
                ? new CanonicalManifestEntry(false, null, null, false)
                : new CanonicalManifestEntry(true, entry.OldHorseId, entry.JraIdentity, entry.HypothesisAccepted))
            .OrderBy(entry => entry.OldHorseId, StringComparer.Ordinal)
            .ThenBy(entry => entry.JraIdentity, StringComparer.Ordinal)
            .ThenBy(entry => entry.HypothesisAccepted)
            .ToArray();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1,
            PreviewFingerprint = previewFingerprint,
            Manifest = new
            {
                manifest.IsOverLimit,
                EntriesPresent = manifest.Entries is not null,
                Entries = canonicalEntries,
            },
        }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(payload));
    }

    internal static bool IsValidRequestKey(Guid? requestKey) => requestKey is null || requestKey != Guid.Empty;

    internal static bool CanCreateIntent(
        HorseIdentityMigrationOperationStatus? status,
        bool hasExistingIntent) =>
        !hasExistingIntent && status == HorseIdentityMigrationOperationStatus.Staged;

    internal static bool CanTransition(
        HorseIdentityMigrationExecutionPhase current,
        HorseIdentityMigrationExecutionPhase next)
    {
        if (current is HorseIdentityMigrationExecutionPhase.Verified or HorseIdentityMigrationExecutionPhase.Failed)
            return false;
        if (next == HorseIdentityMigrationExecutionPhase.Failed)
            return true;

        return (current, next) switch
        {
            (HorseIdentityMigrationExecutionPhase.IntentRecorded, HorseIdentityMigrationExecutionPhase.Preparing) => true,
            (HorseIdentityMigrationExecutionPhase.Preparing, HorseIdentityMigrationExecutionPhase.Draining) => true,
            (HorseIdentityMigrationExecutionPhase.Draining, HorseIdentityMigrationExecutionPhase.StoppingApi) => true,
            (HorseIdentityMigrationExecutionPhase.StoppingApi, HorseIdentityMigrationExecutionPhase.ApiExited) => true,
            (HorseIdentityMigrationExecutionPhase.ApiExited, HorseIdentityMigrationExecutionPhase.BackedUp) => true,
            (HorseIdentityMigrationExecutionPhase.BackedUp, HorseIdentityMigrationExecutionPhase.Applying) => true,
            (HorseIdentityMigrationExecutionPhase.Applying, HorseIdentityMigrationExecutionPhase.DataSucceeded) => true,
            (HorseIdentityMigrationExecutionPhase.DataSucceeded, HorseIdentityMigrationExecutionPhase.Verifying) => true,
            (HorseIdentityMigrationExecutionPhase.Verifying, HorseIdentityMigrationExecutionPhase.Verified) => true,
            _ => false,
        };
    }

    internal static bool CanBeginReadonlyVerification(
        HorseIdentityMigrationOperationStatus operationStatus,
        HorseIdentityMigrationExecutionPhase phase) =>
        operationStatus == HorseIdentityMigrationOperationStatus.Succeeded
        && phase is HorseIdentityMigrationExecutionPhase.DataSucceeded
            or HorseIdentityMigrationExecutionPhase.Verifying;

    private sealed record CanonicalManifestEntry(
        bool IsPresent,
        string? OldHorseId,
        string? JraIdentity,
        bool HypothesisAccepted);
}
