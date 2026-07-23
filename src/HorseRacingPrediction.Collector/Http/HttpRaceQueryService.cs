using System.Net.Http.Json;
using System.Text.Json;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.ApiClient;

namespace HorseRacingPrediction.Collector.Http;

public sealed class HttpRaceQueryService : IRaceQueryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public HttpRaceQueryService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<RaceSearchSummary>> SearchRegisteredRacesAsync(DateOnly raceDate, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient
            .GetAsync($"/api/races?raceDateFrom={raceDate:yyyy-MM-dd}&raceDateTo={raceDate:yyyy-MM-dd}&page=1&pageSize=100&sortBy=raceNumber&sortDescending=false", cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var dto = await response.Content
            .ReadFromJsonAsync<PagedResponseDto<RaceSummaryDto>>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return dto?.Items.Select(x => new RaceSearchSummary(x.RaceId, x.RaceDate, x.RacecourseCode, x.RaceNumber)).ToList() ?? [];
    }

    public async Task<RacePredictionContextReadModel?> GetRacePredictionContextAsync(string raceId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/races/{Uri.EscapeDataString(raceId)}/context", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RacePredictionContextReadModel>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HorseReadModel?> GetHorseAsync(string horseId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/horses/{Uri.EscapeDataString(horseId)}", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<HorseReadModel>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JockeyReadModel?> GetJockeyAsync(string jockeyId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/jockeys/{Uri.EscapeDataString(jockeyId)}", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JockeyReadModel>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TrainerReadModel?> GetTrainerAsync(string trainerId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/trainers/{Uri.EscapeDataString(trainerId)}", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TrainerReadModel>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MemoBySubjectReadModel?> GetMemosBySubjectAsync(string subjectType, string subjectId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient
            .GetAsync($"/api/memos/by-subject/{Uri.EscapeDataString(subjectType)}/{Uri.EscapeDataString(subjectId)}", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var memos = await response.Content.ReadFromJsonAsync<List<MemoResponseDto>>(JsonOptions, cancellationToken).ConfigureAwait(false);
        if (memos is null || memos.Count == 0)
            return null;

        return new MemoBySubjectReadModel
        {
            SubjectKey = $"{subjectType.ToUpperInvariant()}:{subjectId}",
            Memos = memos.Select(m => new MemoSnapshot(
                m.MemoId,
                m.AuthorId,
                m.MemoType,
                m.Content,
                m.CreatedAt,
                m.Subjects.Select(s => new MemoSubjectSnapshot(s.SubjectType, s.SubjectId)).ToList(),
                m.Links.Select(l => new MemoLinkSnapshot(l.LinkId, l.LinkType, l.Title, l.Url, l.StorageKey)).ToList())).ToList()
        };
    }

    public async Task<HorseRaceHistoryReadModel?> GetHorseRaceHistoryAsync(string horseId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/horses/{Uri.EscapeDataString(horseId)}/race-history", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<HorseRaceHistoryReadModel>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JockeyRaceHistoryReadModel?> GetJockeyRaceHistoryAsync(string jockeyId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/jockeys/{Uri.EscapeDataString(jockeyId)}/race-history", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JockeyRaceHistoryReadModel>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MlPredictionResponse?> GetMlPredictionAsync(string raceId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/races/{Uri.EscapeDataString(raceId)}/ml-prediction", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<MlPredictionResponseDto>(JsonOptions, cancellationToken).ConfigureAwait(false);
        if (dto is null || string.IsNullOrWhiteSpace(dto.RaceId))
            return null;

        return new MlPredictionResponse(
            dto.RaceId,
            dto.Rankings.Select(x => new MlHorsePrediction(
                x.EntryId,
                x.HorseId,
                x.HorseNumber,
                x.PredictedScore,
                x.PredictedRank)).ToList());
    }

    public async Task<PredictionTicketSummaryReadModel?> GetPredictionTicketAsync(string predictionTicketId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/predictions/{Uri.EscapeDataString(predictionTicketId)}", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<PredictionTicketResponseDto>(JsonOptions, cancellationToken).ConfigureAwait(false);
        if (dto is null || string.IsNullOrWhiteSpace(dto.PredictionTicketId))
            return null;

        return new PredictionTicketSummaryReadModel(
            dto.PredictionTicketId,
            dto.RaceId,
            dto.PredictorType,
            dto.PredictorId,
            dto.ConfidenceScore,
            dto.SummaryComment,
            dto.PredictedAt,
            dto.Marks.Select(x => new PredictionMarkEntry(
                x.EntryId, x.MarkCode, x.PredictedRank, x.Score, x.Comment)).ToList());
    }

    private sealed record PagedResponseDto<T>(IReadOnlyList<T> Items);

    private sealed record RaceSummaryDto(string RaceId, DateOnly? RaceDate, string? RacecourseCode, int? RaceNumber);
}

internal sealed class PredictionTicketResponseDto
{
    public string PredictionTicketId { get; set; } = string.Empty;
    public string? RaceId { get; set; }
    public string? PredictorType { get; set; }
    public string? PredictorId { get; set; }
    public decimal ConfidenceScore { get; set; }
    public string? SummaryComment { get; set; }
    public DateTimeOffset? PredictedAt { get; set; }
    public List<PredictionMarkResponseDto> Marks { get; set; } = [];
}

internal sealed class PredictionMarkResponseDto
{
    public string EntryId { get; set; } = string.Empty;
    public string MarkCode { get; set; } = string.Empty;
    public int PredictedRank { get; set; }
    public decimal Score { get; set; }
    public string? Comment { get; set; }
}

internal sealed class MemoResponseDto
{
    public string MemoId { get; set; } = string.Empty;
    public string? AuthorId { get; set; }
    public string MemoType { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public List<MemoSubjectDto> Subjects { get; set; } = [];
    public List<MemoLinkDto> Links { get; set; } = [];
}

internal sealed class MemoSubjectDto
{
    public string SubjectType { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
}

internal sealed class MemoLinkDto
{
    public string LinkId { get; set; } = string.Empty;
    public string LinkType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? StorageKey { get; set; }
}

internal sealed class MlPredictionResponseDto
{
    public string RaceId { get; set; } = string.Empty;
    public List<MlHorsePredictionDto> Rankings { get; set; } = [];
}

internal sealed class MlHorsePredictionDto
{
    public string EntryId { get; set; } = string.Empty;
    public string HorseId { get; set; } = string.Empty;
    public int HorseNumber { get; set; }
    public float PredictedScore { get; set; }
    public int PredictedRank { get; set; }
}
