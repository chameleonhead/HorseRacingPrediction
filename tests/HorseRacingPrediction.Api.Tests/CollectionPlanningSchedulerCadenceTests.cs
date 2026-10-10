using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.Options;
using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionPlanningSchedulerCadenceTests
{
    [TestMethod]
    public async Task Discovery_IsFiniteAndRestartDoesNotDuplicateCurrentBucket()
    {
        var directory = Path.Combine(Path.GetTempPath(), "resource-collection-cadence", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            {
                StateDirectory = directory,
            }));
            await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", CollectionResourceType.Race,
                1, "Initial", false);
            var runtime = new CollectionRuntimeStatusRecorder(Enum.GetValues<CollectionRuntimeAction>().Select(action =>
                new CollectionRuntimeActionConfiguration(action, true,
                    action == CollectionRuntimeAction.MetricDelivery ? null : TimeSpan.FromMinutes(1))));
            var scheduler = new CollectionPlanningScheduler(store, runtime);
            var now = new DateTimeOffset(2026, 10, 8, 13, 42, 0, TimeSpan.FromHours(9));

            await scheduler.RunOnceAsync(now, CancellationToken.None);
            var firstProgress = runtime.GetSnapshot().Runtime.Actions.Single(x =>
                x.Action == CollectionRuntimeAction.DiscoveryPlanner).LastProgressAtUtc;
            Assert.IsNotNull(firstProgress);
            scheduler = new CollectionPlanningScheduler(store, runtime);
            await scheduler.RunOnceAsync(now.AddMinutes(1), CancellationToken.None);

            var task = (await store.GetTasksAsync()).Single();
            Assert.AreEqual(new CollectionDefinitionId("race-discovery"), task.Definition);
            Assert.AreEqual(CollectionLane.Realtime, task.Lane);
            Assert.AreEqual("discovery:2026100812", task.Resource.Id);
            var status = runtime.GetSnapshot().Runtime.Actions.Single(x =>
                x.Action == CollectionRuntimeAction.DiscoveryPlanner);
            Assert.AreEqual(0, status.CreatedCount,
                "An already planned bucket is observed, but not reported as a newly created request.");
            Assert.AreEqual(firstProgress, status.LastProgressAtUtc,
                "Re-observing the same durable bucket is not progress.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
