using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.Api.Security;
using Microsoft.Extensions.Options;
using HorseRacingPrediction.Collector.Scheduling;

namespace HorseRacingPrediction.Api.Web.ApiBrowsing;

/// <summary>
/// Calls this same process's own JSON API over HTTP (self-loopback) on behalf of the
/// admin Blazor UI, attaching the process's own configured X-Api-Key automatically.
/// Reuses the Api project's own Contracts DTOs instead of duplicating them.
/// </summary>
public sealed class AdminApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public AdminApiClient(
        HttpClient httpClient,
        AdminApiBaseAddressResolver baseAddressResolver,
        IOptions<ApiKeyOptions> apiKeyOptions)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= baseAddressResolver.Resolve();

        var options = apiKeyOptions.Value;
        if (!string.IsNullOrWhiteSpace(options.Key))
        {
            _httpClient.DefaultRequestHeaders.Remove(options.HeaderName);
            _httpClient.DefaultRequestHeaders.Add(options.HeaderName, options.Key);
        }
    }

    // ------------------------------------------------------------------ //
    // 参照系
    // ------------------------------------------------------------------ //

    public Task<PagedResponse<RaceSummaryResponse>?> SearchRacesAsync(SearchRacesRequest request, CancellationToken cancellationToken = default)
        => GetJsonAsync<PagedResponse<RaceSummaryResponse>>($"/api/races?{BuildQueryString(request)}", cancellationToken);

    public Task<RaceResponse?> GetRaceAsync(string raceId, CancellationToken cancellationToken = default)
        => GetJsonAsync<RaceResponse>($"/api/races/{Uri.EscapeDataString(raceId)}", cancellationToken);

    public Task<PagedResponse<HorseSummaryResponse>?> SearchHorsesAsync(SearchHorsesRequest request, CancellationToken cancellationToken = default)
        => GetJsonAsync<PagedResponse<HorseSummaryResponse>>($"/api/horses?{BuildQueryString(request)}", cancellationToken);

    public Task<HorseProfileResponse?> GetHorseAsync(string horseId, CancellationToken cancellationToken = default)
        => GetJsonAsync<HorseProfileResponse>($"/api/horses/{Uri.EscapeDataString(horseId)}", cancellationToken);

    public Task<HorseRacingPrediction.Contracts.HorseRaceHistoryReadModel?> GetHorseRaceHistoryAsync(
        string horseId,
        CancellationToken cancellationToken = default)
        => GetJsonAsync<HorseRacingPrediction.Contracts.HorseRaceHistoryReadModel>(
            $"/api/horses/{Uri.EscapeDataString(horseId)}/race-history",
            cancellationToken);

    public Task<ParticipationHistoryResponse?> GetHorseParticipationsAsync(string horseId, int? take = null, int? skip = null, CancellationToken cancellationToken = default)
        => GetJsonAsync<ParticipationHistoryResponse>(AppendQueryString($"/api/horses/{Uri.EscapeDataString(horseId)}/participations", new { take, skip }), cancellationToken);

    public Task<PagedResponse<JockeySummaryResponse>?> SearchJockeysAsync(SearchJockeysRequest request, CancellationToken cancellationToken = default)
        => GetJsonAsync<PagedResponse<JockeySummaryResponse>>($"/api/jockeys?{BuildQueryString(request)}", cancellationToken);

    public Task<JockeyProfileResponse?> GetJockeyAsync(string jockeyId, CancellationToken cancellationToken = default)
        => GetJsonAsync<JockeyProfileResponse>($"/api/jockeys/{Uri.EscapeDataString(jockeyId)}", cancellationToken);

    public Task<HorseRacingPrediction.Contracts.JockeyRaceHistoryReadModel?> GetJockeyRaceHistoryAsync(
        string jockeyId,
        CancellationToken cancellationToken = default)
        => GetJsonAsync<HorseRacingPrediction.Contracts.JockeyRaceHistoryReadModel>(
            $"/api/jockeys/{Uri.EscapeDataString(jockeyId)}/race-history",
            cancellationToken);

    public Task<ParticipationHistoryResponse?> GetJockeyParticipationsAsync(string jockeyId, int? take = null, int? skip = null, CancellationToken cancellationToken = default)
        => GetJsonAsync<ParticipationHistoryResponse>(AppendQueryString($"/api/jockeys/{Uri.EscapeDataString(jockeyId)}/participations", new { take, skip }), cancellationToken);

    public Task<PagedResponse<TrainerSummaryResponse>?> SearchTrainersAsync(SearchTrainersRequest request, CancellationToken cancellationToken = default)
        => GetJsonAsync<PagedResponse<TrainerSummaryResponse>>($"/api/trainers?{BuildQueryString(request)}", cancellationToken);

    public Task<TrainerProfileResponse?> GetTrainerAsync(string trainerId, CancellationToken cancellationToken = default)
        => GetJsonAsync<TrainerProfileResponse>($"/api/trainers/{Uri.EscapeDataString(trainerId)}", cancellationToken);

    public Task<ParticipationHistoryResponse?> GetTrainerParticipationsAsync(string trainerId, int? take = null, int? skip = null, CancellationToken cancellationToken = default)
        => GetJsonAsync<ParticipationHistoryResponse>(AppendQueryString($"/api/trainers/{Uri.EscapeDataString(trainerId)}/participations", new { take, skip }), cancellationToken);

    public async Task<IReadOnlyList<OwnerSummaryResponse>> SearchOwnersAsync(string? query, CancellationToken cancellationToken = default)
        => await GetJsonAsync<IReadOnlyList<OwnerSummaryResponse>>($"/api/owners?query={Uri.EscapeDataString(query ?? string.Empty)}", cancellationToken) ?? [];

    public Task<OwnerDetailResponse?> GetOwnerAsync(string ownerId, int? take = null, int? skip = null, CancellationToken cancellationToken = default)
        => GetJsonAsync<OwnerDetailResponse>(AppendQueryString($"/api/owners/{Uri.EscapeDataString(ownerId)}", new { take, skip }), cancellationToken);

    public Task<AdminApiResult> MergeOwnerAsync(string ownerId, MergeOwnerRequest request, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, $"/api/owners/{Uri.EscapeDataString(ownerId)}/merge", request, cancellationToken);

    public Task<AdminApiResult> UpdateOwnerAsync(string ownerId, UpdateOwnerRequest request, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Put, $"/api/owners/{Uri.EscapeDataString(ownerId)}", request, cancellationToken);

    public Task<PagedResponse<PredictionTicketSummaryResponse>?> SearchPredictionsAsync(SearchPredictionTicketsRequest request, CancellationToken cancellationToken = default)
        => GetJsonAsync<PagedResponse<PredictionTicketSummaryResponse>>($"/api/predictions?{BuildQueryString(request)}", cancellationToken);

    public Task<PredictionTicketResponse?> GetPredictionAsync(string predictionTicketId, CancellationToken cancellationToken = default)
        => GetJsonAsync<PredictionTicketResponse>($"/api/predictions/{Uri.EscapeDataString(predictionTicketId)}", cancellationToken);

    public Task<IReadOnlyList<AgentJobStatusReadModel>?> GetJobsAsync(string? jobType = null, AgentJobStatus? status = null, int limit = 100, CancellationToken cancellationToken = default)
        => GetJsonAsync<IReadOnlyList<AgentJobStatusReadModel>>(AppendQueryString("/api/admin/jobs", new { jobType, status, limit }), cancellationToken);

    public Task<AgentJobSearchResult?> SearchJobsAsync(string? view, string? query, string? targetDate, string? jobType, AgentJobStatus? status, int page, int pageSize, CancellationToken cancellationToken = default)
        => GetJsonAsync<AgentJobSearchResult>(AppendQueryString("/api/admin/jobs/search", new { view, query, targetDate, jobType, status, page, pageSize }), cancellationToken);

    public Task<AgentJobDetailReadModel?> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
        => GetJsonAsync<AgentJobDetailReadModel>($"/api/admin/jobs/{Uri.EscapeDataString(jobId)}", cancellationToken);

    public Task<AdminApiResult> RerunJobAsync(string jobId, DateTimeOffset expectedUpdatedAt, string? reason, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, $"/api/admin/jobs/{Uri.EscapeDataString(jobId)}/rerun", new { expectedUpdatedAt, reason }, cancellationToken);

    public Task<AdminApiResult> ReacquireJobAsync(string jobId, DateTimeOffset expectedUpdatedAt, string? reason, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, $"/api/admin/jobs/{Uri.EscapeDataString(jobId)}/reacquire", new { expectedUpdatedAt, reason }, cancellationToken);

    public Task<AdminApiResult> PauseJobsAsync(CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, "/api/admin/jobs/pause", body: null, cancellationToken);

    public Task<AdminApiResult> ResumeJobsAsync(CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, "/api/admin/jobs/resume", body: null, cancellationToken);

    public Task<JobQueueStateResponse?> GetJobQueueStateAsync(CancellationToken cancellationToken = default)
        => GetJsonAsync<JobQueueStateResponse>("/api/admin/jobs/queue-state", cancellationToken);

    public Task<IReadOnlyList<AgentAcquisitionStatusReadModel>?> GetAcquisitionStatusesAsync(
        DateOnly from, DateOnly to, AgentAcquisitionSubjectType? subjectType = null,
        RaceDataCollectionState? status = null, CancellationToken cancellationToken = default)
        => GetJsonAsync<IReadOnlyList<AgentAcquisitionStatusReadModel>>(
            AppendQueryString("/api/collection/acquisitions", new { from, to, subjectType, status }), cancellationToken);

    public Task<AgentAcquisitionStatusReadModel?> GetAcquisitionStatusAsync(string acquisitionKey, CancellationToken cancellationToken = default)
        => GetJsonAsync<AgentAcquisitionStatusReadModel>($"/api/collection/acquisitions/{Uri.EscapeDataString(acquisitionKey)}", cancellationToken);

    public Task<IReadOnlyList<AgentAcquisitionHistoryReadModel>?> GetAcquisitionHistoryAsync(string acquisitionKey, CancellationToken cancellationToken = default)
        => GetJsonAsync<IReadOnlyList<AgentAcquisitionHistoryReadModel>>($"/api/collection/acquisitions/{Uri.EscapeDataString(acquisitionKey)}/history", cancellationToken);

    public async Task<IReadOnlyList<MemoResponse>> GetMemosBySubjectAsync(string subjectType, string subjectId, CancellationToken cancellationToken = default)
        => await GetJsonAsync<IReadOnlyList<MemoResponse>>(
            $"/api/memos/by-subject/{Uri.EscapeDataString(subjectType)}/{Uri.EscapeDataString(subjectId)}",
            cancellationToken).ConfigureAwait(false) ?? Array.Empty<MemoResponse>();

    // ------------------------------------------------------------------ //
    // 馬
    // ------------------------------------------------------------------ //

    public Task<AdminApiResult<string>> RegisterHorseAsync(RegisterHorseRequest request, CancellationToken cancellationToken = default)
        => SendForIdAsync(HttpMethod.Post, "/api/horses", request, (HorseIdResponse r) => r.HorseId, cancellationToken);

    public Task<AdminApiResult> UpdateHorseProfileAsync(string horseId, UpdateHorseProfileRequest request, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Put, $"/api/horses/{Uri.EscapeDataString(horseId)}", request, cancellationToken);

    public Task<AdminApiResult> MergeHorseAliasAsync(string horseId, MergeAliasRequest request, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, $"/api/horses/{Uri.EscapeDataString(horseId)}/aliases", request, cancellationToken);

    public Task<AdminApiResult> CorrectHorseDataAsync(string horseId, CorrectHorseDataRequest request, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Patch, $"/api/horses/{Uri.EscapeDataString(horseId)}", request, cancellationToken);

    // ------------------------------------------------------------------ //
    // 騎手
    // ------------------------------------------------------------------ //

    public Task<AdminApiResult<string>> RegisterJockeyAsync(RegisterJockeyRequest request, CancellationToken cancellationToken = default)
        => SendForIdAsync(HttpMethod.Post, "/api/jockeys", request, (JockeyIdResponse r) => r.JockeyId, cancellationToken);

    public Task<AdminApiResult> UpdateJockeyProfileAsync(string jockeyId, UpdateJockeyProfileRequest request, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Put, $"/api/jockeys/{Uri.EscapeDataString(jockeyId)}", request, cancellationToken);

    public Task<AdminApiResult> MergeJockeyAliasAsync(string jockeyId, MergeAliasRequest request, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, $"/api/jockeys/{Uri.EscapeDataString(jockeyId)}/aliases", request, cancellationToken);

    public Task<AdminApiResult> CorrectJockeyDataAsync(string jockeyId, CorrectJockeyDataRequest request, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Patch, $"/api/jockeys/{Uri.EscapeDataString(jockeyId)}", request, cancellationToken);

    // ------------------------------------------------------------------ //
    // 調教師
    // ------------------------------------------------------------------ //

    public Task<AdminApiResult<string>> RegisterTrainerAsync(RegisterTrainerRequest request, CancellationToken cancellationToken = default)
        => SendForIdAsync(HttpMethod.Post, "/api/trainers", request, (TrainerIdResponse r) => r.TrainerId, cancellationToken);

    public Task<AdminApiResult> UpdateTrainerProfileAsync(string trainerId, UpdateTrainerProfileRequest request, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Put, $"/api/trainers/{Uri.EscapeDataString(trainerId)}", request, cancellationToken);

    public Task<AdminApiResult> MergeTrainerAliasAsync(string trainerId, MergeAliasRequest request, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, $"/api/trainers/{Uri.EscapeDataString(trainerId)}/aliases", request, cancellationToken);

    public Task<AdminApiResult> CorrectTrainerDataAsync(string trainerId, CorrectTrainerDataRequest request, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Patch, $"/api/trainers/{Uri.EscapeDataString(trainerId)}", request, cancellationToken);

    // ------------------------------------------------------------------ //
    // レース
    // ------------------------------------------------------------------ //

    public Task<AdminApiResult> CorrectRaceDataAsync(string raceId, CorrectRaceDataRequest request, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Patch, $"/api/races/{Uri.EscapeDataString(raceId)}", request, cancellationToken);

    // ------------------------------------------------------------------ //
    // 予想票
    // ------------------------------------------------------------------ //

    public Task<AdminApiResult> CorrectPredictionMetadataAsync(string predictionTicketId, CorrectPredictionMetadataRequest request, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Patch, $"/api/predictions/{Uri.EscapeDataString(predictionTicketId)}", request, cancellationToken);

    public Task<AdminApiResult> WithdrawPredictionAsync(string predictionTicketId, WithdrawPredictionTicketRequest request, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, $"/api/predictions/{Uri.EscapeDataString(predictionTicketId)}/withdraw", request, cancellationToken);

    // ------------------------------------------------------------------ //
    // メモ
    // ------------------------------------------------------------------ //

    public Task<AdminApiResult<string>> CreateMemoAsync(CreateMemoRequest request, CancellationToken cancellationToken = default)
        => SendForIdAsync(HttpMethod.Post, "/api/memos", request, (MemoIdResponse r) => r.MemoId, cancellationToken);

    public Task<AdminApiResult> UpdateMemoAsync(string memoId, UpdateMemoRequest request, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Put, $"/api/memos/{Uri.EscapeDataString(memoId)}", request, cancellationToken);

    public Task<AdminApiResult> DeleteMemoAsync(string memoId, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Delete, $"/api/memos/{Uri.EscapeDataString(memoId)}", body: null, cancellationToken);

    public Task<AdminApiResult> ChangeMemoSubjectsAsync(string memoId, ChangeMemoSubjectsRequest request, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Put, $"/api/memos/{Uri.EscapeDataString(memoId)}/subjects", request, cancellationToken);

    // ------------------------------------------------------------------ //
    // 内部ヘルパー
    // ------------------------------------------------------------------ //

    private async Task<T?> GetJsonAsync<T>(string requestUri, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AdminApiResult> SendAsync(HttpMethod method, string requestUri, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: JsonOptions);

        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? AdminApiResult.SuccessResult
            : AdminApiResult.Fail(await ReadErrorsAsync(response, cancellationToken).ConfigureAwait(false));
    }

    private async Task<AdminApiResult<string>> SendForIdAsync<TResponse>(
        HttpMethod method,
        string requestUri,
        object body,
        Func<TResponse, string> selectId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, requestUri)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };

        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return AdminApiResult<string>.Fail(await ReadErrorsAsync(response, cancellationToken).ConfigureAwait(false));

        var parsed = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
        return parsed is null
            ? AdminApiResult<string>.Fail(new[] { "作成には成功しましたが、応答の解析に失敗しました。" })
            : AdminApiResult<string>.Ok(selectId(parsed));
    }

    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var errors = await response.Content.ReadFromJsonAsync<string[]>(JsonOptions, cancellationToken).ConfigureAwait(false);
            if (errors is { Length: > 0 })
                return errors;
        }
        catch (JsonException)
        {
            // レスポンスが想定した配列形式でない場合はステータスコードのみ返す
        }

        return new[] { $"リクエストが失敗しました ({(int)response.StatusCode} {response.ReasonPhrase})。" };
    }

    private static string BuildQueryString(object request)
    {
        var qs = HttpUtility.ParseQueryString(string.Empty);
        foreach (var property in request.GetType().GetProperties())
        {
            var value = property.GetValue(request);
            if (value is null) continue;

            var text = value switch
            {
                DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
                bool b => b ? "true" : "false",
                Enum e => e.ToString(),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture)
            };

            if (!string.IsNullOrEmpty(text))
                qs[property.Name] = text;
        }

        return qs.ToString() ?? string.Empty;
    }

    private static string AppendQueryString(string path, object request)
    {
        var query = BuildQueryString(request);
        return string.IsNullOrWhiteSpace(query) ? path : $"{path}?{query}";
    }

    private sealed record HorseIdResponse(string HorseId);
    private sealed record JockeyIdResponse(string JockeyId);
    private sealed record TrainerIdResponse(string TrainerId);
    private sealed record MemoIdResponse(string MemoId);
}

public sealed record JobQueueStateResponse(bool IsPaused);

public sealed record AdminApiResult(bool Success, IReadOnlyList<string> Errors)
{
    public static readonly AdminApiResult SuccessResult = new(true, Array.Empty<string>());

    public static AdminApiResult Fail(IReadOnlyList<string> errors) => new(false, errors);
}

public sealed record AdminApiResult<T>(bool Success, T? Value, IReadOnlyList<string> Errors)
{
    public static AdminApiResult<T> Ok(T value) => new(true, value, Array.Empty<string>());

    public static AdminApiResult<T> Fail(IReadOnlyList<string> errors) => new(false, default, errors);
}
