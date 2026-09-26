namespace HorseRacingPrediction.Domain.Races;

public partial class RaceAggregate
{
    private void ValidateCollectedEntryAssignments(IReadOnlyList<EntryDetails> entries)
    {
        var numbers = new HashSet<int>();
        var horses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry.HorseNumber <= 0 || !numbers.Add(entry.HorseNumber) || !horses.Add(entry.HorseId))
                throw new ArgumentException("Collected horse numbers and identities must be unique.");
            if (_state.Entries.Any(old =>
                ((old.EntryId == entry.EntryId || old.HorseNumber == entry.HorseNumber) && old.HorseId != entry.HorseId)
                || (old.HorseId == entry.HorseId && (old.HorseNumber != entry.HorseNumber || old.EntryId != entry.EntryId))))
                throw new InvalidOperationException("RaceEntryIdentityMismatch: collection cannot reassign a saved entry to another Horse.");
        }
    }
}
