using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;

namespace HorseRacingPrediction.AgentClient.Web.ApiBrowsing;

public sealed class ApiBrowseClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public ApiBrowseClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<PagedResultDto<RaceSummaryDto>?> SearchRacesAsync(
        DateOnly? from,
        DateOnly? to,
        string? racecourseCode,
        int? raceNumber,
        string? raceName,
        string? winningHorseName,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var qs = HttpUtility.ParseQueryString(string.Empty);
        if (from.HasValue) qs["raceDateFrom"] = from.Value.ToString("yyyy-MM-dd");
        if (to.HasValue) qs["raceDateTo"] = to.Value.ToString("yyyy-MM-dd");
        if (!string.IsNullOrWhiteSpace(racecourseCode)) qs["racecourseCode"] = racecourseCode;
        if (raceNumber.HasValue) qs["raceNumber"] = raceNumber.Value.ToString();
        if (!string.IsNullOrWhiteSpace(raceName)) qs["raceName"] = raceName;
        if (!string.IsNullOrWhiteSpace(winningHorseName)) qs["winningHorseName"] = winningHorseName;
        qs["page"] = page.ToString();
        qs["pageSize"] = pageSize.ToString();
        qs["sortBy"] = "raceDate";
        qs["sortDescending"] = "true";
        return GetJsonAsync<PagedResultDto<RaceSummaryDto>>($"/api/races?{qs}", cancellationToken);
    }

    public Task<RaceDetailDto?> GetRaceAsync(string raceId, CancellationToken cancellationToken = default)
        => GetJsonAsync<RaceDetailDto>($"/api/races/{Uri.EscapeDataString(raceId)}", cancellationToken);

    public Task<PagedResultDto<HorseSummaryDto>?> SearchHorsesAsync(
        string? query,
        string? sexCode,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var qs = HttpUtility.ParseQueryString(string.Empty);
        if (!string.IsNullOrWhiteSpace(query)) qs["query"] = query;
        if (!string.IsNullOrWhiteSpace(sexCode)) qs["sexCode"] = sexCode;
        qs["page"] = page.ToString();
        qs["pageSize"] = pageSize.ToString();
        return GetJsonAsync<PagedResultDto<HorseSummaryDto>>($"/api/horses?{qs}", cancellationToken);
    }

    public Task<HorseProfileDto?> GetHorseAsync(string horseId, CancellationToken cancellationToken = default)
        => GetJsonAsync<HorseProfileDto>($"/api/horses/{Uri.EscapeDataString(horseId)}", cancellationToken);

    public Task<PagedResultDto<JockeySummaryDto>?> SearchJockeysAsync(
        string? query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var qs = HttpUtility.ParseQueryString(string.Empty);
        if (!string.IsNullOrWhiteSpace(query)) qs["query"] = query;
        qs["page"] = page.ToString();
        qs["pageSize"] = pageSize.ToString();
        return GetJsonAsync<PagedResultDto<JockeySummaryDto>>($"/api/jockeys?{qs}", cancellationToken);
    }

    public Task<JockeyProfileDto?> GetJockeyAsync(string jockeyId, CancellationToken cancellationToken = default)
        => GetJsonAsync<JockeyProfileDto>($"/api/jockeys/{Uri.EscapeDataString(jockeyId)}", cancellationToken);

    public Task<PagedResultDto<TrainerSummaryDto>?> SearchTrainersAsync(
        string? query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var qs = HttpUtility.ParseQueryString(string.Empty);
        if (!string.IsNullOrWhiteSpace(query)) qs["query"] = query;
        qs["page"] = page.ToString();
        qs["pageSize"] = pageSize.ToString();
        return GetJsonAsync<PagedResultDto<TrainerSummaryDto>>($"/api/trainers?{qs}", cancellationToken);
    }

    public Task<TrainerProfileDto?> GetTrainerAsync(string trainerId, CancellationToken cancellationToken = default)
        => GetJsonAsync<TrainerProfileDto>($"/api/trainers/{Uri.EscapeDataString(trainerId)}", cancellationToken);

    private async Task<T?> GetJsonAsync<T>(string requestUri, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }
}
