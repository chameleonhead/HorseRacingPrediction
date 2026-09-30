using HorseRacingPrediction.Contracts.Common;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record SearchRacesResponse(IReadOnlyList<RaceSummaryDto> Races, PaginationDto Pagination);
