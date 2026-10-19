using HorseRacingPrediction.Api.IdentityMigration;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityMigrationPreparationTests
{
    [TestMethod]
    public void PreparationRequiresStablePauseZeroRunningAndFullProductionDuration()
    {
        var pauseStartedAt = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
        var stablePause = new CollectionPipelineState(true, "operator", pauseStartedAt);

        Assert.IsFalse(HorseIdentityMigrationPreparationService.IsReadyToStop(stablePause, pauseStartedAt,
            runningCount: 0, pauseStartedAt + TimeSpan.FromSeconds(899)));
        Assert.IsTrue(HorseIdentityMigrationPreparationService.IsReadyToStop(stablePause, pauseStartedAt,
            runningCount: 0, pauseStartedAt + TimeSpan.FromSeconds(900)));
        Assert.IsFalse(HorseIdentityMigrationPreparationService.IsReadyToStop(stablePause, pauseStartedAt,
            runningCount: 1, pauseStartedAt + TimeSpan.FromHours(1)));
        Assert.IsFalse(HorseIdentityMigrationPreparationService.IsReadyToStop(
            stablePause with { UpdatedAt = pauseStartedAt.AddSeconds(1) }, pauseStartedAt,
            runningCount: 0, pauseStartedAt + TimeSpan.FromHours(1)));
        Assert.AreEqual(TimeSpan.FromSeconds(900), HorseIdentityMigrationPreparationService.RequiredPauseDuration);
    }
}
