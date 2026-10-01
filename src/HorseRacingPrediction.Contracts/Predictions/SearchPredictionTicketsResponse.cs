using HorseRacingPrediction.Contracts.Common;

namespace HorseRacingPrediction.Contracts.Predictions;

public sealed record SearchPredictionTicketsResponse(
    IReadOnlyList<PredictionTicketSummaryDto> PredictionTickets,
    PaginationDto Pagination);
