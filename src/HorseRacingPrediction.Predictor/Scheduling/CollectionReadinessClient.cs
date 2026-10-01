using System.Net.Http.Json;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Predictor.Scheduling;

public sealed class CollectionReadinessClient(HttpClient client)
{
    public async Task<CollectionReadinessSnapshot> GetAsync(string raceId,
        CancellationToken cancellationToken = default)
    {
        var response = await client.GetFromJsonAsync<GetRaceCollectionReadinessResponse>(
            $"api/v2/admin/collection/races/{Uri.EscapeDataString(raceId)}/readiness", cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Collection readiness response was empty.");

        return new CollectionReadinessSnapshot(response.Readiness.PendingHorseRequests,
            response.Readiness.PendingJockeyRequests, response.Readiness.PendingRaceResultRequests,
            response.Readiness.PendingTrainerRequests);
    }
}

public sealed class PredictionExecutionOptions
{
    public const string SectionName = "AgentProcessing";
    public bool Enabled { get; set; } = true;
    public bool EnablePredictionExecution { get; set; } = true;
    public bool BlockPredictionWhileHistoricalRequestsPending { get; set; } = true;
    public int PredictionIntervalMinutes { get; set; } = 5;
    public int PredictionMinAgeMinutes { get; set; }
    public int PredictionBatchSize { get; set; } = 10;
    public int PredictionLeaseMinutes { get; set; } = 15;
    public int HistoricalRequestRetryDelayMinutes { get; set; } = 5;
}
