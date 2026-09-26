namespace HorseRacingPrediction.Domain.Races;

public partial class RaceAggregate
{
    private void ValidateCollectedEntryAssignments(IReadOnlyList<EntryDetails> entries)
    {
        var numbers = new HashSet<int>();
        var horses = new HashSet<string>(StringComparer.Ordinal);
        var entryIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.HorseId) || string.IsNullOrWhiteSpace(entry.EntryId)
                || entry.HorseNumber is <= 0 || entry.GateNumber is < 1 or > 8
                || (entry.HorseNumber is { } number && !numbers.Add(number)) || !horses.Add(entry.HorseId)
                || !entryIds.Add(entry.EntryId))
                throw new ArgumentException("Collected horse numbers and identities must be unique.");
            if (_state.Entries.Any(old =>
                (old.EntryId == entry.EntryId && old.HorseId != entry.HorseId)
                || (old.HorseId == entry.HorseId && old.EntryId != entry.EntryId)))
                throw new InvalidOperationException("RaceEntryIdentityMismatch: collection cannot reassign a saved entry to another Horse.");
        }
        // Validate the effective full set, so a partial update cannot duplicate an unchanged number.
        var effective = _state.Entries.Where(old => !horses.Contains(old.HorseId))
            .Concat(entries.Select(entry => entry with
            {
                HorseNumber = entry.HorseNumber ?? _state.Entries.FirstOrDefault(old => old.HorseId == entry.HorseId)?.HorseNumber
            })).Where(entry => entry.HorseNumber.HasValue).ToArray();
        if (effective.Select(entry => entry.HorseNumber).Distinct().Count() != effective.Length)
            throw new ArgumentException("Horse numbers must be unique within the race.");
    }
}
