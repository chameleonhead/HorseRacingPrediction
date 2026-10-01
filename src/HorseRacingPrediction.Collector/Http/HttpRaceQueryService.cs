using System.Net.Http.Json;
using System.Text.Json;
using HorseRacingPrediction.ApiClient;

using HorseRacingPrediction.Contracts.Horses;
using HorseRacingPrediction.Contracts.Jockeys;
using HorseRacingPrediction.Contracts.MachineLearning;
using HorseRacingPrediction.Contracts.Memos;
using HorseRacingPrediction.Contracts.Predictions;
using HorseRacingPrediction.Contracts.Races;
using HorseRacingPrediction.Contracts.Trainers;

namespace HorseRacingPrediction.Collector.Http;

public sealed class HttpRaceQueryService : IRaceQueryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public HttpRaceQueryService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<RaceSearchSummaryDto>> SearchRegisteredRacesAsync(DateOnly raceDate, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient
            .GetAsync($"/api/races?raceDateFrom={raceDate:yyyy-MM-dd}&raceDateTo={raceDate:yyyy-MM-dd}&page=1&pageSize=100&sortBy=raceNumber&sortDescending=false", cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var dto = await response.Content
            .ReadFromJsonAsync<SearchRacesResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return dto?.Races.Select(x => new RaceSearchSummaryDto(x.RaceId, x.RaceDate, x.RacecourseCode, x.RaceNumber)).ToList() ?? [];
    }

    public async Task<RacePredictionContextDto?> GetRacePredictionContextAsync(string raceId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/races/{Uri.EscapeDataString(raceId)}/context", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var result = await response.Content
            .ReadFromJsonAsync<GetRacePredictionContextResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return result?.Context;
    }

    public async Task<HorseDto?> GetHorseAsync(string horseId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/horses/{Uri.EscapeDataString(horseId)}", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var result = await response.Content
            .ReadFromJsonAsync<GetHorseProfileResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return result?.Horse;
    }

    public async Task<JockeyDto?> GetJockeyAsync(string jockeyId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/jockeys/{Uri.EscapeDataString(jockeyId)}", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var result = await response.Content
            .ReadFromJsonAsync<GetJockeyProfileResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return result?.Jockey;
    }

    public async Task<TrainerDto?> GetTrainerAsync(string trainerId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/trainers/{Uri.EscapeDataString(trainerId)}", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var result = await response.Content
            .ReadFromJsonAsync<GetTrainerProfileResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return result?.Trainer;
    }

    public async Task<MemoBySubjectDto?> GetMemosBySubjectAsync(string subjectType, string subjectId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient
            .GetAsync($"/api/memos/by-subject/{Uri.EscapeDataString(subjectType)}/{Uri.EscapeDataString(subjectId)}", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<GetMemosBySubjectResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
        var memos = payload?.Memos;
        if (memos is null || memos.Count == 0)
            return null;

        return new MemoBySubjectDto
        {
            SubjectKey = $"{subjectType.ToUpperInvariant()}:{subjectId}",
            Memos = memos.Select(m => new MemoDto(
                m.MemoId,
                m.AuthorId,
                m.MemoType,
                m.Content,
                m.CreatedAt,
                m.Subjects,
                m.Links)).ToList()
        };
    }

    public async Task<HorseRaceHistoryDto?> GetHorseRaceHistoryAsync(string horseId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/horses/{Uri.EscapeDataString(horseId)}/race-history", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<HorseRaceHistoryDto>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JockeyRaceHistoryDto?> GetJockeyRaceHistoryAsync(string jockeyId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/jockeys/{Uri.EscapeDataString(jockeyId)}/race-history", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JockeyRaceHistoryDto>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MlPredictionDto?> GetMlPredictionAsync(string raceId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/races/{Uri.EscapeDataString(raceId)}/ml-prediction", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<GetMlPredictionResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
        var dto = payload?.Prediction;
        if (dto is null || string.IsNullOrWhiteSpace(dto.RaceId))
            return null;

        return dto;
    }

    public async Task<PredictionTicketWithMarksDto?> GetPredictionTicketAsync(string predictionTicketId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/predictions/{Uri.EscapeDataString(predictionTicketId)}", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<GetPredictionTicketResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
        var dto = payload?.PredictionTicket;
        if (dto is null || string.IsNullOrWhiteSpace(dto.PredictionTicketId))
            return null;

        return new PredictionTicketWithMarksDto(
            dto.PredictionTicketId,
            dto.RaceId,
            dto.PredictorType,
            dto.PredictorId,
            dto.ConfidenceScore,
            dto.SummaryComment,
            dto.PredictedAt,
            dto.Marks.Select(x => new PredictionMarkEntryDto(
                x.EntryId, x.MarkCode, x.PredictedRank, x.Score, x.Comment)).ToList());
    }

}
