using System.Net.Http.Json;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Predictor.Scheduling;

public sealed class CollectionReadinessClient(HttpClient client)
{
    public async Task<CollectionReadinessSnapshot> GetAsync(string raceId,
        CancellationToken cancellationToken = default)
        => await client.GetFromJsonAsync<CollectionReadinessSnapshot>(
            $"api/admin/collection/readiness/{Uri.EscapeDataString(raceId)}", cancellationToken)
           ?? throw new InvalidOperationException("Collection readiness response was empty.");
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
