using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Domain.Tests;

[TestClass]
public sealed class RaceAggregateBulkCollectionTests
{
    [TestMethod]
    public void ApplyBulkRaceResult_NewRace_AppliesCompleteEnvelope()
    {
        var aggregate = new RaceAggregate(RaceId.New);
        var observedAt = new DateTimeOffset(2026, 9, 15, 15, 30, 0, TimeSpan.FromHours(9));
        var data = CreateData(observedAt);

        aggregate.ApplyBulkRaceResult(data);

        var details = aggregate.GetDetails();
        Assert.AreEqual(RaceStatus.PayoutDeclared, details.Status);
        Assert.AreEqual("G1", details.GradeCode);
        Assert.AreEqual(1, details.Entries.Count);
        Assert.AreEqual("horse-1", details.Entries[0].HorseId);
        Assert.AreEqual(1, details.EntryResults.Count);
        Assert.AreEqual(1, details.EntryResults[0].FinishPosition);
        Assert.AreEqual("Horse One", details.WinningHorseName);
        Assert.AreEqual(1, details.WeatherObservations.Count);
        Assert.AreEqual(1, details.TrackConditionObservations.Count);
        Assert.IsNotNull(details.PayoutResult);
    }

    [TestMethod]
    public void ApplyBulkRaceResult_InvalidResultReference_EmitsNothing()
    {
        var aggregate = new RaceAggregate(RaceId.New);
        var observedAt = DateTimeOffset.UtcNow;
        var data = CreateData(observedAt) with
        {
            EntryResults = [new("entry-missing", 1, null, null, null, null, null, null)]
        };

        Assert.Throws<ArgumentException>(() => aggregate.ApplyBulkRaceResult(data));

        Assert.IsNull(aggregate.GetDetails().RaceDate);
        Assert.AreEqual(0, aggregate.Version);
    }

    [TestMethod]
    public void ApplyBulkRaceResult_DuplicateEntryId_EmitsNothing()
    {
        var aggregate = new RaceAggregate(RaceId.New);
        var observedAt = DateTimeOffset.UtcNow;
        var entry = CreateData(observedAt).Entries[0];
        var data = CreateData(observedAt) with { Entries = [entry, entry] };

        Assert.Throws<ArgumentException>(() => aggregate.ApplyBulkRaceResult(data));

        Assert.IsNull(aggregate.GetDetails().RaceDate);
        Assert.AreEqual(0, aggregate.Version);
    }

    [TestMethod]
    public void ApplyBulkRaceResult_ExistingEntry_MergesLaterCollectedValuesWithoutErasingIdentity()
    {
        var aggregate = new RaceAggregate(RaceId.New);
        var observedAt = DateTimeOffset.UtcNow;
        var initial = CreateData(observedAt) with
        {
            Entries = [new("entry-1", "horse-1", 1, null, null, null, null, null, null, null, null, null, null)],
            EntryResults = [],
            WinningHorseName = null,
            DeclaredAt = null,
            Payouts = null
        };
        aggregate.ApplyBulkRaceResult(initial);
        var versionAfterInitial = aggregate.Version;

        var enriched = initial with
        {
            Entries = [new("entry-1", "horse-1", 1, "jockey-1", "trainer-1", 1, 56m, "M", 4, 480m, 2m, "FRONT", "Owner One")]
        };
        aggregate.ApplyBulkRaceResult(enriched);

        var entry = aggregate.GetDetails().Entries.Single();
        Assert.AreEqual("horse-1", entry.HorseId);
        Assert.AreEqual(1, entry.HorseNumber);
        Assert.AreEqual("jockey-1", entry.JockeyId);
        Assert.AreEqual("trainer-1", entry.TrainerId);
        Assert.AreEqual("Owner One", entry.OwnerName);
        Assert.IsTrue(aggregate.Version > versionAfterInitial);
    }

    [TestMethod]
    public void ApplyBulkRaceResult_ReplayAndNullOwner_AreIdempotentAndNonDestructive()
    {
        var aggregate = new RaceAggregate(RaceId.New);
        var data = CreateData(DateTimeOffset.UtcNow) with
        {
            Entries = [CreateData(DateTimeOffset.UtcNow).Entries[0] with { OwnerName = "Owner One" }],
            EntryResults = [],
            WinningHorseName = null,
            DeclaredAt = null,
            Payouts = null
        };
        aggregate.ApplyBulkRaceResult(data);
        var version = aggregate.Version;

        aggregate.ApplyBulkRaceResult(data);
        Assert.AreEqual(version, aggregate.Version);

        aggregate.ApplyBulkRaceResult(data with
        {
            Entries = [data.Entries[0] with { OwnerName = null }]
        });
        Assert.AreEqual(version, aggregate.Version);
        Assert.AreEqual("Owner One", aggregate.GetDetails().Entries.Single().OwnerName);
    }

    [TestMethod]
    public void CollectedIdentityMismatch_EmitsNoEventsInEitherPath()
    {
        var aggregate = new RaceAggregate(RaceId.New);
        var original = CreateData(DateTimeOffset.UtcNow);
        aggregate.ApplyBulkRaceResult(original);
        var version = aggregate.Version;
        var swapped = original.Entries[0] with { HorseId = "horse-other", OwnerName = "Wrong owner" };
        Assert.ThrowsExactly<InvalidOperationException>(() => aggregate.ApplyBulkRaceResult(
            original with { Entries = [swapped], RaceName = "Must not update" }));
        Assert.AreEqual(version, aggregate.Version);
        Assert.ThrowsExactly<InvalidOperationException>(() => aggregate.RefreshCollectedData(
            new("Must not update", null, null, null, null, 1, [swapped], null)));
        Assert.AreEqual(version, aggregate.Version);
        Assert.AreEqual("horse-1", aggregate.GetDetails().Entries.Single().HorseId);
        Assert.AreEqual(original.RaceName, aggregate.GetDetails().RaceName);
    }

    private static BulkRaceResultData CreateData(DateTimeOffset observedAt) => new(
        new DateOnly(2026, 9, 15), "NAKAYAMA", 11, "Collected race", 1,
        "G1", "TURF", 2000, "RIGHT",
        [new("entry-1", "horse-1", 1, "jockey-1", "trainer-1", 1, 56m, "M", 4, 480m, 2m, null)],
        [new("entry-1", 1, "1:59.9", null, "34.0", null, 1000m, null)],
        "Horse One", observedAt,
        new(observedAt, [new("1", 250m)], [], [], [], []),
        new(observedAt, "SUNNY", "晴", 25m, 50m, "N", 2m),
        new(observedAt, "GOOD", null, "良"));
}
