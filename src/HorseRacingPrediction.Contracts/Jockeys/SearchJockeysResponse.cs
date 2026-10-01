using HorseRacingPrediction.Contracts.Common;

namespace HorseRacingPrediction.Contracts.Jockeys;

public sealed record SearchJockeysResponse(IReadOnlyList<JockeySummaryDto> Jockeys, PaginationDto Pagination);
