using HorseRacingPrediction.Contracts.Common;

namespace HorseRacingPrediction.Contracts.Horses;

public sealed record SearchHorsesResponse(IReadOnlyList<HorseSummaryDto> Horses, PaginationDto Pagination);
