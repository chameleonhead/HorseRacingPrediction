namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed class RaceTextInsightCollector
{
    public Task CollectForRaceAsync(string raceId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
