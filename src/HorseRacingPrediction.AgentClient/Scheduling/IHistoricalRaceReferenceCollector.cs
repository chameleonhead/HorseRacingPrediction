namespace HorseRacingPrediction.AgentClient.Scheduling;

public interface IHistoricalRaceReferenceCollector
{
    Task<IReadOnlyList<HistoricalRaceReference>> CollectAsync(
        DateOnly raceDate,
        string racecourse,
        int raceNumber,
        CancellationToken cancellationToken = default);
}