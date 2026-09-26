using System.Reflection;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Infrastructure.Persistence;

namespace HorseRacingPrediction.Infrastructure.Tests;

[TestClass]
public sealed class RaceRepairProjectionComparisonTests
{
    [TestMethod]
    public void WeightHistoryComparison_NormalizesOffsetsButRejectsDifferentInstantOrWeight()
    {
        var utc = new DateTimeOffset(2026, 9, 26, 6, 24, 0, TimeSpan.Zero).AddTicks(1234567);
        var expected = new HorseWeightEntry("race-test", "entry-test", utc, 470m, 2m);
        var compare = typeof(RaceEntryRepairInspector).GetMethod("Compare", BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(HorseWeightEntry[]));
        var blockers = new List<string>();
        void Check(HorseWeightEntry actual) => compare.Invoke(null, ["WeightHistory", new[] { expected }, new[] { actual }, blockers]);

        Check(expected with { RecordedAt = utc.ToOffset(TimeSpan.FromHours(9)) });
        Assert.IsEmpty(blockers);
        Check(expected with { RecordedAt = utc.AddTicks(1).ToOffset(TimeSpan.FromHours(9)) });
        Assert.HasCount(1, blockers);
        blockers.Clear();
        Check(expected with { RecordedAt = utc.ToOffset(TimeSpan.FromHours(9)), DeclaredWeight = 471m });
        Assert.HasCount(1, blockers);
    }
}
