using System.Net.Http.Json;
using System.Text.Json.Serialization;
using HorseRacingPrediction.Agents.Plugins;

namespace HorseRacingPrediction.AgentClient.Http;

/// <summary>
/// クラウド API を呼び出して <see cref="IPredictionWriteService"/> を実装するクラス。
/// </summary>
public sealed class HttpPredictionWriteService : IPredictionWriteService
{
    private readonly HttpClient _httpClient;

    public HttpPredictionWriteService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> CreatePredictionTicketAsync(
        string raceId,
        string predictorType,
        string predictorId,
        decimal confidenceScore,
        string? summaryComment,
        CancellationToken cancellationToken = default)
    {
        var predictionTicketId = $"prediction-{Guid.NewGuid():D}";
        var request = new
        {
            PredictionTicketId = predictionTicketId,
            RaceId = raceId,
            PredictorType = predictorType,
            PredictorId = predictorId,
            ConfidenceScore = confidenceScore,
            SummaryComment = summaryComment
        };

        var response = await _httpClient
            .PostAsJsonAsync("/api/predictions", request, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<CreatedIdResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result?.PredictionTicketId ?? predictionTicketId;
    }

    public async Task AddPredictionMarkAsync(
        string predictionTicketId,
        string entryId,
        string markCode,
        int predictedRank,
        decimal score,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        var request = new
        {
            EntryId = entryId,
            MarkCode = markCode,
            PredictedRank = predictedRank,
            Score = score,
            Comment = comment
        };

        var response = await _httpClient
            .PostAsJsonAsync($"/api/predictions/{Uri.EscapeDataString(predictionTicketId)}/marks", request, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
    }

    public async Task AddPredictionRationaleAsync(
        string predictionTicketId,
        string subjectType,
        string subjectId,
        string signalType,
        string? signalValue,
        string? explanationText,
        CancellationToken cancellationToken = default)
    {
        var request = new
        {
            SubjectType = subjectType,
            SubjectId = subjectId,
            SignalType = signalType,
            SignalValue = signalValue,
            ExplanationText = explanationText
        };

        var response = await _httpClient
            .PostAsJsonAsync($"/api/predictions/{Uri.EscapeDataString(predictionTicketId)}/rationales", request, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
    }

    public async Task FinalizePredictionTicketAsync(
        string predictionTicketId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient
            .PostAsync($"/api/predictions/{Uri.EscapeDataString(predictionTicketId)}/finalize", null, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
    }

    private sealed class CreatedIdResponse
    {
        [JsonPropertyName("predictionTicketId")]
        public string? PredictionTicketId { get; init; }
    }
}
