using HorseRacingPrediction.Agents.Contracts;

namespace HorseRacingPrediction.Api.Contracts;

public sealed class SearchRacesRequest
{
    public string? RaceId { get; init; }
    public DateOnly? RaceDateFrom { get; init; }
    public DateOnly? RaceDateTo { get; init; }
    public string? RacecourseCode { get; init; }
    public int? RaceNumber { get; init; }
    public string? RaceName { get; init; }
    public RaceStatus? Status { get; init; }
    public string? WinningHorseName { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
    public string? SortBy { get; init; }
    public bool? SortDescending { get; init; }
}