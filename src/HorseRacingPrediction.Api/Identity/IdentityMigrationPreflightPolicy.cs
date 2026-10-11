using HorseRacingPrediction.Contracts.Identity;

namespace HorseRacingPrediction.Api.Identity;

internal static class IdentityMigrationPreflightPolicy
{
    internal const int PolicyVersion = 1;
    internal const int PageSize = 128;
    internal const int MaximumCursorCharacters = 4 * 1024;
    internal const int MaximumCursorPayloadBytes = 3 * 1024;
    internal const int MaximumKeyCharacters = 512;
    internal const int MaximumSchemaAssumptionsCharacters = 1024;
    internal const int MaximumPayloadBytes = 64 * 1024;
    internal const int MaximumJsonDepth = 16;
    internal static readonly TimeSpan RequestDeadline = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan CursorLifetime = TimeSpan.FromMinutes(30);

    internal const string StoreSelection =
        "event-store;collection-platform;prediction-schedule;local-queue-if-configured";

    internal const string SchemaAssumptionVersion = "identity-migration-preflight-six-source-v1";

    private static readonly IdentityMigrationPreflightPhase[] Phases =
    [
        IdentityMigrationPreflightPhase.HorseRoots,
        IdentityMigrationPreflightPhase.HorseEvents,
        IdentityMigrationPreflightPhase.JraProfiles,
        IdentityMigrationPreflightPhase.HorseRaceHistories,
        IdentityMigrationPreflightPhase.HorseWeightHistories,
        IdentityMigrationPreflightPhase.CollectionResources,
    ];

    internal static IReadOnlyList<IdentityMigrationPreflightPhase> GetPhases() => Array.AsReadOnly(Phases);

    internal static IdentityMigrationPreflightPhase? GetNextPhase(IdentityMigrationPreflightPhase phase)
    {
        var index = Array.IndexOf(Phases, phase);
        return index < 0 || index == Phases.Length - 1 ? null : Phases[index + 1];
    }

    internal static bool IsCanonicalHorseId(string? value)
    {
        const string prefix = "horse-";
        if (value is null || !value.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var idText = value[prefix.Length..];
        return Guid.TryParseExact(idText, "D", out var id)
            && string.Equals(idText, id.ToString("D"), StringComparison.Ordinal);
    }

    internal static bool IsCanonicalJraIdentity(string? value)
    {
        const string prefix = "jra-horse:";
        if (value is null || !value.StartsWith(prefix, StringComparison.Ordinal)
            || value.Length != prefix.Length + 10)
            return false;

        for (var index = prefix.Length; index < value.Length; index++)
        {
            if (value[index] is < '0' or > '9') return false;
        }

        return true;
    }

    internal static IdentityMigrationPreflightObservationStatus ClassifyIdentityEvidence(
        bool rootObserved,
        string? initialCanonicalJraIdentity,
        string? currentCanonicalJraIdentity)
    {
        if (!rootObserved) return IdentityMigrationPreflightObservationStatus.Unverifiable;

        var initialPresent = !string.IsNullOrEmpty(initialCanonicalJraIdentity);
        var currentPresent = !string.IsNullOrEmpty(currentCanonicalJraIdentity);
        if (initialPresent && !IsCanonicalJraIdentity(initialCanonicalJraIdentity)
            || currentPresent && !IsCanonicalJraIdentity(currentCanonicalJraIdentity))
            return IdentityMigrationPreflightObservationStatus.UnknownShape;

        if (initialPresent && currentPresent
            && !string.Equals(initialCanonicalJraIdentity, currentCanonicalJraIdentity, StringComparison.Ordinal))
            return IdentityMigrationPreflightObservationStatus.Conflict;

        return initialPresent || currentPresent
            ? IdentityMigrationPreflightObservationStatus.Observed
            : IdentityMigrationPreflightObservationStatus.Unverifiable;
    }
}
