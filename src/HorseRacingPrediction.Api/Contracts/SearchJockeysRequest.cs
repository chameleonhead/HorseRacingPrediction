namespace HorseRacingPrediction.Api.Contracts;

public sealed class SearchJockeysRequest
{
    public string? JockeyId { get; init; }
    public string? Query { get; init; }
    public string? DisplayName { get; init; }
    public string? NormalizedName { get; init; }
    public string? AffiliationCode { get; init; }
    public string? AliasValue { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
    public string? SortBy { get; init; }
    public bool? SortDescending { get; init; }
}