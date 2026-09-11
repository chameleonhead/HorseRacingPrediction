using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionPlanningSchedulerCadenceTests
{
    [TestMethod]
    public async Task RaceDiscovery_IsRegisteredByNewScheduler()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("RUN_15_MINUTE_CADENCE_TEST"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Inconclusive("リリース前の実時間検証時だけ RUN_15_MINUTE_CADENCE_TEST=1 で実行します。");
        }

        var directory = Path.Combine(Path.GetTempPath(), "resource-collection-cadence", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = Options.Create(new CollectionPlatformOptions
            {
                StateDirectory = directory,
            });
            var store = new CollectionPlatformStore(options);
            await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", ResourceType.Race,
                1, "Initial", false);
            var scheduler = new CollectionPlanningScheduler(store);
            await scheduler.StartAsync(CancellationToken.None);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                var tasks = await store.GetTasksAsync();
                Assert.HasCount(1, tasks);
                var task = tasks[0];
                Assert.AreEqual(new CollectionDefinitionId("race-discovery"), task.Definition);
                Assert.AreEqual(CollectionLane.Realtime, task.Lane);

                // A completed/failed task remains represented by CollectionState. Restarting the
                // planner in the same three-hour bucket must not create another request.
                await scheduler.StopAsync(CancellationToken.None);
                scheduler.Dispose();
                scheduler = new CollectionPlanningScheduler(store);
                await scheduler.StartAsync(CancellationToken.None);
                await Task.Delay(TimeSpan.FromSeconds(1));
                Assert.HasCount(1, await store.GetTasksAsync());
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
