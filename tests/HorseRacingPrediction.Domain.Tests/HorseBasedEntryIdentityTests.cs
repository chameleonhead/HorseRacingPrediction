using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Domain.Tests;

[TestClass]
public sealed class HorseBasedEntryIdentityTests
{
    [TestMethod]
    public void RegisterEntry_AllowsUnknownHorseNumber()
    {
        var aggregate = CreatePublishedRace();

        aggregate.RegisterEntry("entry-a", "horse-a", horseNumber: null, gateNumber: null);

        var entry = aggregate.GetDetails().Entries.Single();
        Assert.AreEqual("entry-a", entry.EntryId);
        Assert.AreEqual("horse-a", entry.HorseId);
        Assert.IsNull(entry.HorseNumber);
    }

    [TestMethod]
    public void EntryRegistered_AllowsUnknownHorseNumber()
    {
        var registered = new EntryRegistered("entry-a", "horse-a", horseNumber: null);

        Assert.IsNull(registered.HorseNumber);
    }

    [TestMethod]
    public void RegisterEntry_RejectsDuplicateHorseAndNumberWithoutEvents()
    {
        var aggregate = CreatePublishedRace();
        aggregate.RegisterEntry("entry-a", "horse-a", 1, gateNumber: 1);
        var version = aggregate.Version;

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            aggregate.RegisterEntry("entry-b", "horse-a", 2, gateNumber: 2));
        Assert.AreEqual(version, aggregate.Version);

        Assert.ThrowsExactly<ArgumentException>(() =>
            aggregate.RegisterEntry("entry-b", "horse-b", 1, gateNumber: 1));
        Assert.AreEqual(version, aggregate.Version);
    }

    [TestMethod]
    public void RegisterEntry_RejectsInvalidNumberAndGateWithoutEvents()
    {
        var aggregate = CreatePublishedRace();

        Assert.ThrowsExactly<ArgumentException>(() =>
            aggregate.RegisterEntry("entry-negative", "horse-negative", -1, gateNumber: 1));
        Assert.AreEqual(2, aggregate.Version);

        Assert.ThrowsExactly<ArgumentException>(() =>
            aggregate.RegisterEntry("entry-gate", "horse-gate", 1, gateNumber: 9));
        Assert.AreEqual(2, aggregate.Version);
    }

    [TestMethod]
    public void RegisterEntry_AllowsSameHorseNumberUpdateAndPreservesKnownValuesForNulls()
    {
        var aggregate = CreatePublishedRace();
        aggregate.RegisterEntry("entry-a", "horse-a", 1, gateNumber: 1);

        aggregate.RegisterEntry("entry-a", "horse-a", 2, gateNumber: 2);
        var updated = aggregate.GetDetails().Entries.Single();
        Assert.AreEqual(2, updated.HorseNumber);
        Assert.AreEqual(2, updated.GateNumber);

        aggregate.RegisterEntry("entry-a", "horse-a", null, gateNumber: null);
        var preserved = aggregate.GetDetails().Entries.Single();
        Assert.AreEqual(2, preserved.HorseNumber);
        Assert.AreEqual(2, preserved.GateNumber);
    }

    [TestMethod]
    public void ApplyBulkRaceResult_AllowsUnconfirmedEntriesThenConfirmsAndSwapsNumbers()
    {
        var aggregate = new RaceAggregate(RaceId.New);
        var observedAt = DateTimeOffset.UtcNow;

        aggregate.ApplyBulkRaceResult(CreateEnvelope(observedAt,
            Entry("entry-a", "horse-a", null, null),
            Entry("entry-b", "horse-b", null, null)));
        Assert.IsNull(aggregate.GetDetails().Entries.Single(x => x.HorseId == "horse-a").HorseNumber);
        Assert.IsNull(aggregate.GetDetails().Entries.Single(x => x.HorseId == "horse-b").HorseNumber);

        aggregate.ApplyBulkRaceResult(CreateEnvelope(observedAt,
            Entry("entry-a", "horse-a", 1, 1),
            Entry("entry-b", "horse-b", 2, 2)));

        var confirmed = aggregate.GetDetails();
        Assert.AreEqual(1, confirmed.Entries.Single(x => x.HorseId == "horse-a").HorseNumber);
        Assert.AreEqual(2, confirmed.Entries.Single(x => x.HorseId == "horse-b").HorseNumber);

        aggregate.ApplyBulkRaceResult(CreateEnvelope(observedAt,
            Entry("entry-a", "horse-a", 2, 2),
            Entry("entry-b", "horse-b", 1, 1),
            includeResult: true));

        var swapped = aggregate.GetDetails();
        Assert.AreEqual(2, swapped.Entries.Single(x => x.HorseId == "horse-a").HorseNumber);
        Assert.AreEqual(1, swapped.Entries.Single(x => x.HorseId == "horse-b").HorseNumber);
        Assert.AreEqual("entry-a", swapped.Entries.Single(x => x.HorseId == "horse-a").EntryId);
        Assert.AreEqual("entry-b", swapped.Entries.Single(x => x.HorseId == "horse-b").EntryId);
        Assert.AreEqual("entry-a", swapped.EntryResults.Single(x => x.FinishPosition == 1).EntryId);
    }

    [TestMethod]
    public void ApplyBulkRaceResult_NullNumberPreservesConfirmedNumber()
    {
        var aggregate = new RaceAggregate(RaceId.New);
        var observedAt = DateTimeOffset.UtcNow;
        aggregate.ApplyBulkRaceResult(CreateEnvelope(observedAt,
            Entry("entry-a", "horse-a", 1, 1),
            Entry("entry-b", "horse-b", 2, 2)));

        aggregate.ApplyBulkRaceResult(CreateEnvelope(observedAt,
            Entry("entry-a", "horse-a", null, null),
            Entry("entry-b", "horse-b", null, null)));

        var details = aggregate.GetDetails();
        Assert.AreEqual(1, details.Entries.Single(x => x.HorseId == "horse-a").HorseNumber);
        Assert.AreEqual(2, details.Entries.Single(x => x.HorseId == "horse-b").HorseNumber);
    }

    [TestMethod]
    public void CollectedEntryIdentityConflictsAreRejectedBeforeEvents()
    {
        var aggregate = new RaceAggregate(RaceId.New);
        var observedAt = DateTimeOffset.UtcNow;
        var original = CreateEnvelope(observedAt,
            Entry("entry-a", "horse-a", 1, 1),
            Entry("entry-b", "horse-b", 2, 2));
        aggregate.ApplyBulkRaceResult(original);
        var version = aggregate.Version;

        Assert.ThrowsExactly<ArgumentException>(() => aggregate.ApplyBulkRaceResult(original with
        {
            Entries = [
                original.Entries[0] with { HorseNumber = 2 },
                original.Entries[1]
            ]
        }));
        Assert.AreEqual(version, aggregate.Version);

        Assert.ThrowsExactly<ArgumentException>(() => aggregate.ApplyBulkRaceResult(original with
        {
            Entries = [
                original.Entries[0],
                original.Entries[1] with { EntryId = "entry-b-duplicate", HorseId = "horse-a" }
            ]
        }));
        Assert.AreEqual(version, aggregate.Version);

        Assert.ThrowsExactly<InvalidOperationException>(() => aggregate.ApplyBulkRaceResult(original with
        {
            Entries = [original.Entries[0] with { HorseId = "horse-other" }, original.Entries[1]]
        }));
        Assert.AreEqual(version, aggregate.Version);
        Assert.AreEqual("horse-a", aggregate.GetDetails().Entries.Single(x => x.EntryId == "entry-a").HorseId);
    }

    private static RaceAggregate CreatePublishedRace()
    {
        var aggregate = new RaceAggregate(RaceId.New);
        aggregate.Create(new DateOnly(2026, 9, 26), "TOKYO", 1, "Test Race");
        aggregate.PublishCard(2);
        return aggregate;
    }

    private static BulkRaceResultData CreateEnvelope(
        DateTimeOffset observedAt,
        EntryDetails first,
        EntryDetails second,
        bool includeResult = false)
        => new(
            new DateOnly(2026, 9, 26), "TOKYO", 1, "Test Race", 2,
            null, null, null, null,
            [first, second],
            includeResult ? [new("entry-a", 1, "1:59.0", null, null, null, null, null)] : [],
            includeResult ? "Horse A" : null,
            includeResult ? observedAt : null);

    private static EntryDetails Entry(string entryId, string horseId, int? horseNumber, int? gateNumber)
        => new(entryId, horseId, horseNumber, null, null, gateNumber, null, null, null, null, null, null);
}
