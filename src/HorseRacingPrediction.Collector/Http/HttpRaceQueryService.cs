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
        var memos = await response.Content.ReadFromJsonAsync<List<MemoResponseDto>>(JsonOptions, cancellationToken).ConfigureAwait(false);
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
                m.Subjects.Select(s => new HorseRacingPrediction.Contracts.Memos.MemoSubjectDto(s.SubjectType, s.SubjectId)).ToList(),
                m.Links.Select(l => new HorseRacingPrediction.Contracts.Memos.MemoLinkDto(l.LinkId, l.LinkType, l.Title, l.Url, l.StorageKey)).ToList())).ToList()
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
        var dto = await response.Content.ReadFromJsonAsync<MlPredictionResponseDto>(JsonOptions, cancellationToken).ConfigureAwait(false);
        if (dto is null || string.IsNullOrWhiteSpace(dto.RaceId))
            return null;

        return new MlPredictionDto(
            dto.RaceId,
            dto.Rankings.Select(x => new HorseRacingPrediction.Contracts.MachineLearning.MlHorsePredictionDto(
                x.EntryId,
                x.HorseId,
                x.HorseNumber,
                x.PredictedScore,
                x.PredictedRank)).ToList());
    }

    public async Task<PredictionTicketWithMarksDto?> GetPredictionTicketAsync(string predictionTicketId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/predictions/{Uri.EscapeDataString(predictionTicketId)}", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<PredictionTicketResponseDto>(JsonOptions, cancellationToken).ConfigureAwait(false);
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
