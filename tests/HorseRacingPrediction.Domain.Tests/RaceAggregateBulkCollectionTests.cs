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
