using HorseRacingPrediction.Contracts.Common;

namespace HorseRacingPrediction.Contracts.Trainers;

public sealed record SearchTrainersResponse(IReadOnlyList<TrainerSummaryDto> Trainers, PaginationDto Pagination);
