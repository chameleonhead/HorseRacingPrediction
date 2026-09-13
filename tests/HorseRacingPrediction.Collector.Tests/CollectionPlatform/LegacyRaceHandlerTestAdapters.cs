using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Collector.Tests.TestSupport;
using HorseRacingPrediction.PredictionScheduling;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Workflow;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

// Keeps the pre-unification behavioral tests readable while routing every assertion through
// the production race-detail handler. These are test-only adapters, not runtime compatibility handlers.
internal sealed class JraRaceCardCollectionHandler
{
    private readonly JraRaceDetailCollectionHandler _inner;

    public JraRaceCardCollectionHandler(IJraSessionFactory sessions,
        JraRaceCardCollectionWorkflowFactory workflows, IPredictionSchedule? predictionSchedule = null,
        ICollectionRequestSink? requests = null, TimeProvider? timeProvider = null)
    {
        var results = new FakeJraRaceResultCollectionWorkflow
        {
            ResultFactory = race => new RaceResultCollectionResult(race, "created-race", [1], [],
                "https://example.test/result", true),
        };
        _inner = new(sessions, workflows, _ => results, predictionSchedule, requests, timeProvider);
    }

    public Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task, CancellationToken token)
        => _inner.CollectAsync(task with
        {
            Resource = new(ResourceType.Race, task.Resource.Provider, task.Resource.Id),
            Definition = new("race-detail"),
        }, token);
}

internal sealed class JraRaceResultCollectionHandler
{
    private readonly JraRaceDetailCollectionHandler _inner;

    public JraRaceResultCollectionHandler(IJraSessionFactory sessions,
        JraRaceResultCollectionWorkflowFactory workflows)
    {
        _inner = new(sessions, _ => new FakeJraRaceCardCollectionWorkflow(), workflows,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero)));
    }

    public Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task, CancellationToken token)
        => _inner.CollectAsync(task with
        {
            Resource = new(ResourceType.Race, task.Resource.Provider, task.Resource.Id),
            Definition = new("race-detail"),
        }, token);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
