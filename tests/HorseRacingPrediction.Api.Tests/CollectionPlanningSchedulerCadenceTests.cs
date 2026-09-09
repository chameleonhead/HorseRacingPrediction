using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionPlanningSchedulerCadenceTests
{
    [TestMethod]
    public async Task AcquisitionPlanReview_ActualCadence_IsFifteenMinutesForFourIntervals()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("RUN_15_MINUTE_CADENCE_TEST"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Inconclusive("リリース前の実時間検証時だけ RUN_15_MINUTE_CADENCE_TEST=1 で実行します。");
        }

        var directory = Path.Combine(Path.GetTempPath(), "collection-cadence", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = Options.Create(new AgentProcessingOptions
            {
                StateDirectory = directory,
                AcquisitionPlanReviewIntervalMinutes = 15,
                EnableAutonomousHistoricalCollection = true,
            });
            var store = new ProcessingStateStore(options, NullLogger<ProcessingStateStore>.Instance);
            var scheduler = new CollectionPlanningScheduler(store, new CollectionMaintenanceState(), options);
            await scheduler.StartAsync(CancellationToken.None);
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(81));
                IReadOnlyList<AgentJobStatusReadModel> reviews = [];
                // 起動直後の初回ジョブから最初の15分境界までは部分区間になり得る。
                // それを除外できるよう6回分を観測し、末尾の完全な4区間を評価する。
                while (reviews.Count < 6)
                {
                    await Task.Delay(TimeSpan.FromSeconds(20), timeout.Token);
                    reviews = await store.GetJobStatusesAsync(
                        AgentJobType.AcquisitionPlanReview, null, 10, timeout.Token);
                }

                var ordered = reviews.OrderBy(x => x.FirstQueuedAt).ToList();
                var lastFourIntervals = ordered.Zip(ordered.Skip(1), (before, after) => after.FirstQueuedAt - before.FirstQueuedAt)
                    .TakeLast(4)
                    .ToList();
                Assert.HasCount(4, lastFourIntervals);
                Assert.IsTrue(
                    lastFourIntervals.All(x => x >= TimeSpan.FromMinutes(14) && x <= TimeSpan.FromMinutes(16)),
                    $"Intervals={string.Join(", ", lastFourIntervals)}");
            }
            finally
            {
                await scheduler.StopAsync(CancellationToken.None);
                scheduler.Dispose();
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
