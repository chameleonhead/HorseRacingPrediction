using System.Text.Json;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Domain.Tests;

[TestClass]
public sealed class RaceParticipationStatusTests
{
    [TestMethod]
    public void ParticipationStatus_HasStableValues_AndLegacyEntryEventDefaultsToActive()
    {
        Assert.AreEqual(0, Convert.ToInt32(Enum.Parse<RaceEntryParticipationStatus>("Active")));
        Assert.AreEqual(1, Convert.ToInt32(Enum.Parse<RaceEntryParticipationStatus>("Cancelled")));
        Assert.AreEqual(2, Convert.ToInt32(Enum.Parse<RaceEntryParticipationStatus>("Excluded")));

        var legacy = JsonSerializer.Deserialize<EntryRegistered>(
            "{\"EntryId\":\"entry-a\",\"HorseId\":\"horse-a\",\"HorseNumber\":6}");

        Assert.IsNotNull(legacy);
        Assert.AreEqual(RaceEntryParticipationStatus.Active, legacy!.ParticipationStatus);
    }

    [TestMethod]
    public void RegisterEntry_PersistsStatus_OmittedStatusPreservesCancellation_AndExplicitActiveReactivates()
    {
        var aggregate = CreatePublishedRace();

        aggregate.RegisterEntry("entry-a", "horse-a", 6,
            gateNumber: 3, participationStatus: RaceEntryParticipationStatus.Cancelled);
        aggregate.RegisterEntry("entry-a", "horse-a", null, gateNumber: null);

        var stillCancelled = aggregate.GetDetails().Entries.Single();
        Assert.AreEqual(RaceEntryParticipationStatus.Cancelled, stillCancelled.ParticipationStatus);
        Assert.AreEqual(6, stillCancelled.HorseNumber);
        Assert.AreEqual(3, stillCancelled.GateNumber);

        aggregate.RegisterEntry("entry-a", "horse-a", null, gateNumber: null,
            participationStatus: RaceEntryParticipationStatus.Active);

        Assert.AreEqual(RaceEntryParticipationStatus.Active,
            aggregate.GetDetails().Entries.Single().ParticipationStatus);
    }

    [TestMethod]
    public void RegisterEntry_RejectsUnknownStatusBeforeEmitting()
    {
        var aggregate = CreatePublishedRace();
        var version = aggregate.Version;

        Assert.ThrowsExactly<ArgumentException>(() => aggregate.RegisterEntry(
            "entry-a", "horse-a", 6,
            participationStatus: (RaceEntryParticipationStatus)99));

        Assert.AreEqual(version, aggregate.Version);
        Assert.IsFalse(aggregate.GetDetails().Entries.Any());
    }

    [TestMethod]
    public void RefreshCollectedData_NullStatusAndNumbersPreserveKnownEntryState()
    {
        var aggregate = CreatePublishedRace();
        aggregate.RegisterEntry("entry-a", "horse-a", 6,
            gateNumber: 3, participationStatus: RaceEntryParticipationStatus.Cancelled);

        aggregate.RefreshCollectedData(new CollectedRaceData(
            "Test Race", null, null, null, null, 1,
            [Entry("entry-a", "horse-a", null, null)], null));

        var entry = aggregate.GetDetails().Entries.Single();
        Assert.AreEqual("entry-a", entry.EntryId);
        Assert.AreEqual("horse-a", entry.HorseId);
        Assert.AreEqual(6, entry.HorseNumber);
        Assert.AreEqual(3, entry.GateNumber);
        Assert.AreEqual(RaceEntryParticipationStatus.Cancelled, entry.ParticipationStatus);

        aggregate.RefreshCollectedData(new CollectedRaceData(
            "Test Race", null, null, null, null, 1,
            [Entry("entry-a", "horse-a", null, null, RaceEntryParticipationStatus.Active)], null));

        Assert.AreEqual(RaceEntryParticipationStatus.Active,
            aggregate.GetDetails().Entries.Single().ParticipationStatus);
    }

    [TestMethod]
    public void ApplyBulkRaceResult_NullStatusAndNumberPreserveCancelledEntryState()
    {
        var aggregate = new RaceAggregate(RaceId.New);
        var observedAt = DateTimeOffset.UtcNow;
        aggregate.ApplyBulkRaceResult(CreateEnvelope(observedAt,
            Entry("entry-a", "horse-a", 6, 3, RaceEntryParticipationStatus.Cancelled)));

        aggregate.ApplyBulkRaceResult(CreateEnvelope(observedAt,
            Entry("entry-a", "horse-a", null, null)));

        var entry = aggregate.GetDetails().Entries.Single();
        Assert.AreEqual("entry-a", entry.EntryId);
        Assert.AreEqual("horse-a", entry.HorseId);
        Assert.AreEqual(6, entry.HorseNumber);
        Assert.AreEqual(3, entry.GateNumber);
        Assert.AreEqual(RaceEntryParticipationStatus.Cancelled, entry.ParticipationStatus);

        aggregate.ApplyBulkRaceResult(CreateEnvelope(observedAt,
            Entry("entry-a", "horse-a", null, null, RaceEntryParticipationStatus.Active)));

        Assert.AreEqual(RaceEntryParticipationStatus.Active,
            aggregate.GetDetails().Entries.Single().ParticipationStatus);
    }

    [TestMethod]
    public void ApplyBulkRaceResult_OmittedResultEntryStatusDoesNotReactivateCancelledEntry()
    {
        var aggregate = new RaceAggregate(RaceId.New);
        var observedAt = DateTimeOffset.UtcNow;
        aggregate.ApplyBulkRaceResult(CreateEnvelope(observedAt,
            Entry("entry-a", "horse-a", 6, 3, RaceEntryParticipationStatus.Cancelled)));

        aggregate.ApplyBulkRaceResult(CreateEnvelope(observedAt,
            Entry("entry-a", "horse-a", null, null), includeResult: true));

        Assert.AreEqual(RaceEntryParticipationStatus.Cancelled,
            aggregate.GetDetails().Entries.Single().ParticipationStatus);
    }

    [TestMethod]
    public void RecordOddsSnapshot_IgnoresCancelledEntryWithUnknownNumber()
    {
        var aggregate = CreatePublishedRace();
        aggregate.RegisterEntry("entry-active", "horse-active", 1, gateNumber: 1);
        aggregate.RegisterEntry("entry-cancelled", "horse-cancelled", null,
            participationStatus: RaceEntryParticipationStatus.Cancelled);

        aggregate.RecordOddsSnapshot(DateTimeOffset.UtcNow, [new(1, 2.5m)]);

        Assert.IsTrue(aggregate.GetDetails().Entries.Single(x => x.EntryId == "entry-cancelled").HorseNumber is null);
    }

    [TestMethod]
    public void RecordOddsSnapshot_RejectsSelectionForCancelledKnownNumber()
    {
        var aggregate = CreatePublishedRace();
        aggregate.RegisterEntry("entry-active", "horse-active", 1, gateNumber: 1);
        aggregate.RegisterEntry("entry-cancelled", "horse-cancelled", 6, gateNumber: 3,
            participationStatus: RaceEntryParticipationStatus.Cancelled);

        Assert.ThrowsExactly<ArgumentException>(() =>
            aggregate.RecordOddsSnapshot(DateTimeOffset.UtcNow,
                [new(1, 2.5m), new(6, 8.0m)]));
    }

    private static RaceAggregate CreatePublishedRace()
    {
        var aggregate = new RaceAggregate(RaceId.New);
        aggregate.Create(new DateOnly(2026, 9, 26), "TOKYO", 1, "Test Race");
        aggregate.PublishCard(2);
        return aggregate;
    }

    private static BulkRaceResultData CreateEnvelope(
        DateTimeOffset observedAt, EntryDetails entry, bool includeResult = false)
        => new(
            new DateOnly(2026, 9, 26), "TOKYO", 1, "Test Race", 2,
            null, null, null, null, [entry],
            includeResult ? [new("entry-a", 1, "1:59.0", null, null, null, null, null)] : [],
            includeResult ? "Horse A" : null,
            includeResult ? observedAt : null);

    private static EntryDetails Entry(string entryId, string horseId, int? horseNumber,
        int? gateNumber, RaceEntryParticipationStatus? participationStatus = null)
        => new(entryId, horseId, horseNumber, null, null, gateNumber, null, null, null,
            null, null, null, null, participationStatus);
}
