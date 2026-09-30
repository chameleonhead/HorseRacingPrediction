namespace HorseRacingPrediction.Contracts.Common;

public sealed record PaginationDto(int Page, int PageSize, int TotalCount, int TotalPages);
