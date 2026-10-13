using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Identity;

namespace HorseRacingPrediction.Api.Identity;

internal static class HorseIdentityMigrationManifestPolicy
{
    internal const int MaximumEntries = HorseIdentityMigrationManifest.MaximumEntries;
    private const string JraIdentityPrefix = "jra-horse:";

    internal static HorseIdentityMigrationManifestValidation Validate(HorseIdentityMigrationManifest? manifest)
    {
        var errors = new List<HorseIdentityMigrationManifestError>();
        var entries = manifest?.Entries;
        if (entries is null)
        {
            errors.Add(new(HorseIdentityMigrationManifestErrorCode.Required, null));
            return Invalid(errors);
        }

        if (entries.Count == 0)
            errors.Add(new(HorseIdentityMigrationManifestErrorCode.Empty, null));
        if (manifest!.IsOverLimit || entries.Count > MaximumEntries)
        {
            errors.Add(new(HorseIdentityMigrationManifestErrorCode.TooManyEntries, null));
            return Invalid(errors);
        }

        var validatedInputs = new List<(string OldHorseId, string JraIdentity, int Index)>();
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry is null)
            {
                errors.Add(new(HorseIdentityMigrationManifestErrorCode.MissingEntry, index));
                continue;
            }

            var valid = true;
            if (!IdentityMigrationPreflightPolicy.IsCanonicalHorseId(entry.OldHorseId))
            {
                errors.Add(new(HorseIdentityMigrationManifestErrorCode.InvalidOldHorseId, index));
                valid = false;
            }

            if (!IdentityMigrationPreflightPolicy.IsCanonicalJraIdentity(entry.JraIdentity))
            {
                errors.Add(new(HorseIdentityMigrationManifestErrorCode.InvalidJraIdentity, index));
                valid = false;
            }

            if (!entry.HypothesisAccepted)
            {
                errors.Add(new(HorseIdentityMigrationManifestErrorCode.HypothesisNotAccepted, index));
                valid = false;
            }

            if (valid)
                validatedInputs.Add((entry.OldHorseId!, entry.JraIdentity!, index));
        }

        if (validatedInputs.Count != entries.Count)
            return Invalid(errors);

        foreach (var group in validatedInputs.GroupBy(entry => entry.OldHorseId, StringComparer.Ordinal))
        {
            if (group.Count() < 2) continue;
            var code = group.Select(entry => entry.JraIdentity).Distinct(StringComparer.Ordinal).Skip(1).Any()
                ? HorseIdentityMigrationManifestErrorCode.ConflictingIdentityForOldHorse
                : HorseIdentityMigrationManifestErrorCode.DuplicateOldHorse;
            foreach (var item in group)
                errors.Add(new(code, item.Index));
        }

        if (errors.Count != 0)
            return Invalid(errors);

        var mappings = new List<HorseIdentityMigrationMapping>(validatedInputs.Count);
        foreach (var entry in validatedInputs)
        {
            var destination = BuildDestinationHorseId(entry.JraIdentity);
            if (string.Equals(entry.OldHorseId, destination, StringComparison.Ordinal))
                errors.Add(new(HorseIdentityMigrationManifestErrorCode.SelfMap, entry.Index));
            mappings.Add(new(entry.OldHorseId, entry.JraIdentity, destination));
        }

        foreach (var group in mappings.GroupBy(mapping => mapping.DestinationHorseId, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            foreach (var mapping in group)
                errors.Add(new(HorseIdentityMigrationManifestErrorCode.DuplicateDestination, IndexOf(validatedInputs, mapping.OldHorseId)));
        }

        var edges = mappings.ToDictionary(mapping => mapping.OldHorseId, mapping => mapping.DestinationHorseId,
            StringComparer.Ordinal);
        foreach (var mapping in mappings)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = mapping.OldHorseId;
            while (edges.TryGetValue(current, out var next))
            {
                if (!visited.Add(current))
                {
                    errors.Add(new(HorseIdentityMigrationManifestErrorCode.Cycle, IndexOf(validatedInputs, mapping.OldHorseId)));
                    break;
                }

                current = next;
            }
        }

        return errors.Count == 0
            ? new HorseIdentityMigrationManifestValidation(Array.AsReadOnly(mappings.ToArray()), Array.Empty<HorseIdentityMigrationManifestError>())
            : Invalid(errors);
    }

    private static string BuildDestinationHorseId(string jraIdentity)
    {
        // BuildHorseId consumes the shared JRA URL form, while the accepted manifest carries
        // the stable canonical horse number. The synthetic routing prefix is never persisted
        // or exposed; JraSourceIdentity validates and extracts only that canonical number.
        var horseNumber = jraIdentity[JraIdentityPrefix.Length..];
        var sourceUrl = $"https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud00{horseNumber}/manifest";
        var normalizedUrl = JraSourceIdentity.NormalizeHorseUrl(sourceUrl)
            ?? throw new InvalidOperationException("A validated canonical JRA identity did not normalize.");
        if (!JraSourceIdentity.TryNormalizeHorse(normalizedUrl.ToString(), out var normalizedIdentity)
            || !string.Equals(normalizedIdentity, jraIdentity, StringComparison.Ordinal))
            throw new InvalidOperationException("A validated JRA identity did not round-trip through the shared normalizer.");

        return DeterministicIdGenerator.BuildHorseId("migration", normalizedUrl.ToString());
    }

    private static int IndexOf(IReadOnlyList<(string OldHorseId, string JraIdentity, int Index)> entries, string oldHorseId) =>
        entries.First(entry => string.Equals(entry.OldHorseId, oldHorseId, StringComparison.Ordinal)).Index;

    private static HorseIdentityMigrationManifestValidation Invalid(List<HorseIdentityMigrationManifestError> errors) =>
        new(Array.Empty<HorseIdentityMigrationMapping>(), Array.AsReadOnly(errors.ToArray()));
}

internal sealed record HorseIdentityMigrationMapping(string OldHorseId, string JraIdentity, string DestinationHorseId);

internal sealed record HorseIdentityMigrationManifestError(
    HorseIdentityMigrationManifestErrorCode Code,
    int? EntryIndex);

internal sealed record HorseIdentityMigrationManifestValidation(
    IReadOnlyList<HorseIdentityMigrationMapping> Mappings,
    IReadOnlyList<HorseIdentityMigrationManifestError> Errors)
{
    internal bool IsValid => Errors.Count == 0;
}

internal enum HorseIdentityMigrationManifestErrorCode
{
    Required,
    Empty,
    TooManyEntries,
    MissingEntry,
    InvalidOldHorseId,
    InvalidJraIdentity,
    HypothesisNotAccepted,
    DuplicateOldHorse,
    ConflictingIdentityForOldHorse,
    DuplicateDestination,
    SelfMap,
    Cycle,
}
