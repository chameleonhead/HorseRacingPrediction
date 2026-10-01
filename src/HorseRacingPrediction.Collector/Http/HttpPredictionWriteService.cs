using System.Net.Http.Json;
using HorseRacingPrediction.ApiClient;

using HorseRacingPrediction.Contracts.Predictions;

namespace HorseRacingPrediction.Collector.Http;

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
        => await CreateBoundPredictionTicketAsync(raceId, predictorType, predictorId, confidenceScore,
            summaryComment, null, cancellationToken).ConfigureAwait(false);

    public async Task<string> CreateBoundPredictionTicketAsync(string raceId, string predictorType,
        string predictorId, decimal confidenceScore, string? summaryComment, string? entryAssignmentFingerprint,
        CancellationToken cancellationToken = default)
    {
        var predictionTicketId = $"predictionticket-{Guid.NewGuid():D}";
        var request = new CreatePredictionTicketRequest(new CreatePredictionTicketInputDto(
            raceId, predictorType, predictorId, confidenceScore, summaryComment,
            predictionTicketId, entryAssignmentFingerprint));

        var response = await _httpClient
            .PostAsJsonAsync("/api/predictions", request, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<CreatePredictionTicketResponse>(cancellationToken: cancellationToken)
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
        var request = new AddPredictionMarkRequest(predictionTicketId,
            new AddPredictionMarkInputDto(entryId, markCode, predictedRank, score, comment));

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
        var request = new AddPredictionRationaleRequest(predictionTicketId,
            new AddPredictionRationaleInputDto(subjectType, subjectId, signalType, signalValue, explanationText));

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
}
