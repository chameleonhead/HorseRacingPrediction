using System.Net.Http.Json;
using System.Text.Json;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Application.Queries.ReadModels;

namespace HorseRacingPrediction.AgentClient.Http;

/// <summary>
/// クラウド API を呼び出して <see cref="IRaceQueryService"/> を実装するクラス。
/// </summary>
public sealed class HttpRaceQueryService : IRaceQueryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public HttpRaceQueryService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<RacePredictionContextReadModel?> GetRacePredictionContextAsync(
        string raceId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient
            .GetAsync($"/api/races/{Uri.EscapeDataString(raceId)}/context", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var dto = await response.Content
            .ReadFromJsonAsync<RacePredictionContextDto>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return ReadModelMapper.ToRacePredictionContext(dto);
    }

    public async Task<HorseReadModel?> GetHorseAsync(
        string horseId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient
            .GetAsync($"/api/horses/{Uri.EscapeDataString(horseId)}", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var dto = await response.Content
            .ReadFromJsonAsync<HorseDto>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return ReadModelMapper.ToHorse(dto);
    }

    public async Task<JockeyReadModel?> GetJockeyAsync(
        string jockeyId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient
            .GetAsync($"/api/jockeys/{Uri.EscapeDataString(jockeyId)}", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var dto = await response.Content
            .ReadFromJsonAsync<JockeyDto>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return ReadModelMapper.ToJockey(dto);
    }

    public async Task<MemoBySubjectReadModel?> GetMemosBySubjectAsync(
        string subjectType, string subjectId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient
            .GetAsync($"/api/memos/by-subject/{Uri.EscapeDataString(subjectType)}/{Uri.EscapeDataString(subjectId)}", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var memos = await response.Content
            .ReadFromJsonAsync<List<MemoResponseDto>>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        var subjectKey = $"{subjectType.ToUpperInvariant()}:{subjectId}";
        return ReadModelMapper.ToMemoBySubject(subjectKey, memos);
    }

    public async Task<HorseRaceHistoryReadModel?> GetHorseRaceHistoryAsync(
        string horseId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient
            .GetAsync($"/api/horses/{Uri.EscapeDataString(horseId)}/race-history", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var dto = await response.Content
            .ReadFromJsonAsync<HorseRaceHistoryDto>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return ReadModelMapper.ToHorseRaceHistory(dto);
    }

    public async Task<JockeyRaceHistoryReadModel?> GetJockeyRaceHistoryAsync(
        string jockeyId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient
            .GetAsync($"/api/jockeys/{Uri.EscapeDataString(jockeyId)}/race-history", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var dto = await response.Content
            .ReadFromJsonAsync<JockeyRaceHistoryDto>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return ReadModelMapper.ToJockeyRaceHistory(dto);
    }
}
