using HorseRacingPrediction.AgentClient.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.AgentClient.Tests.Scheduling;

[TestClass]
public sealed class ProcessingStateStoreTests
{
    private string _stateDirectory = null!;

    [TestInitialize]
    public void Setup()
    {
        _stateDirectory = Path.Combine(Path.GetTempPath(), "agent-client-state-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_stateDirectory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_stateDirectory))
        {
            Directory.Delete(_stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task TakeReadyPredictionCandidatesAsync_DoesNotLoseCandidateAcrossRestart_WhenLeaseExpires()
    {
        var now = new DateTimeOffset(2026, 5, 12, 12, 0, 0, TimeSpan.Zero);
        var sut = CreateStore(predictionLeaseMinutes: 5);

        await sut.EnqueuePredictionCandidatesAsync(["race-1"], now);

        var firstTake = await sut.TakeReadyPredictionCandidatesAsync(now.AddMinutes(11), TimeSpan.FromMinutes(10), 10);

        CollectionAssert.AreEqual(new[] { "race-1" }, firstTake.ToArray());

        var restarted = CreateStore(predictionLeaseMinutes: 5);
        var beforeLeaseExpiry = await restarted.TakeReadyPredictionCandidatesAsync(now.AddMinutes(15), TimeSpan.FromMinutes(10), 10);
        Assert.IsEmpty(beforeLeaseExpiry, "lease 中は再取得されないこと");

        var afterLeaseExpiry = await restarted.TakeReadyPredictionCandidatesAsync(now.AddMinutes(17), TimeSpan.FromMinutes(10), 10);
        CollectionAssert.AreEqual(new[] { "race-1" }, afterLeaseExpiry.ToArray());
    }

    [TestMethod]
    public async Task MarkPredictionCompletedAsync_RemovesInProgressEntry()
    {
        var now = new DateTimeOffset(2026, 5, 12, 12, 0, 0, TimeSpan.Zero);
        var sut = CreateStore(predictionLeaseMinutes: 5);

        await sut.EnqueuePredictionCandidatesAsync(["race-1"], now);
        await sut.TakeReadyPredictionCandidatesAsync(now.AddMinutes(11), TimeSpan.FromMinutes(10), 10);

        await sut.MarkPredictionCompletedAsync("race-1");

        var restarted = CreateStore(predictionLeaseMinutes: 5);
        var afterCompletion = await restarted.TakeReadyPredictionCandidatesAsync(now.AddMinutes(30), TimeSpan.FromMinutes(10), 10);

        Assert.IsEmpty(afterCompletion, "完了済みは再取得されないこと");
    }

    private ProcessingStateStore CreateStore(int predictionLeaseMinutes)
    {
        var options = Options.Create(new AgentProcessingOptions
        {
            StateDirectory = _stateDirectory,
            PredictionLeaseMinutes = predictionLeaseMinutes
        });

        return new ProcessingStateStore(options, NullLogger<ProcessingStateStore>.Instance);
    }
}