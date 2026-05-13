namespace HorseRacingPrediction.Api.Contracts;

public sealed class SearchHorsesRequest
{
    public string? HorseId { get; init; }
    public string? Query { get; init; }
    public string? RegisteredName { get; init; }
    public string? NormalizedName { get; init; }
    public string? SexCode { get; init; }
    public DateOnly? BirthDateFrom { get; init; }
    public DateOnly? BirthDateTo { get; init; }
    public string? AliasValue { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
    public string? SortBy { get; init; }
    public bool? SortDescending { get; init; }
}