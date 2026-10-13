namespace HorseRacingPrediction.Contracts.Identity;

/// <summary>An operator-confirmed old Horse ID and canonical JRA identity hypothesis.</summary>
public sealed record HorseIdentityMigrationManifestEntry(
    string? OldHorseId,
    string? JraIdentity,
    bool HypothesisAccepted);

/// <summary>
/// Immutable snapshot of one bounded migration request. Destination IDs are deliberately
/// absent from the input and are derived by the server's shared identity generator.
/// </summary>
public sealed class HorseIdentityMigrationManifest
{
    public const int MaximumEntries = 16;
    private const int OverflowSentinelEntryCount = MaximumEntries + 1;

    public HorseIdentityMigrationManifest(IReadOnlyList<HorseIdentityMigrationManifestEntry?>? entries)
    {
        if (entries is null)
            return;

        IsOverLimit = entries.Count > MaximumEntries;
        var retainedCount = Math.Min(entries.Count, OverflowSentinelEntryCount);
        var snapshot = new HorseIdentityMigrationManifestEntry?[retainedCount];
        for (var index = 0; index < retainedCount; index++)
            snapshot[index] = entries[index];
        Entries = Array.AsReadOnly(snapshot);
    }

    public IReadOnlyList<HorseIdentityMigrationManifestEntry?>? Entries { get; }
    public bool IsOverLimit { get; }
}
