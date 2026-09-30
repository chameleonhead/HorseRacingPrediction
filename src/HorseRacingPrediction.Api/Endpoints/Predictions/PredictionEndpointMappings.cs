using AppReadModels = HorseRacingPrediction.Application.Queries.ReadModels;

using HorseRacingPrediction.Contracts.Predictions;

namespace HorseRacingPrediction.Api.Endpoints.Predictions;

internal static class PredictionEndpointMappings
{
    internal static IOrderedEnumerable<AppReadModels.PredictionTicketReadModel>? SortPredictionTickets(
        IEnumerable<AppReadModels.PredictionTicketReadModel> source,
        SearchPredictionTicketsRequest request)
        => (request.SortBy ?? "predictedAt").ToLowerInvariant() switch
        {
            "predictedat" => (request.SortDescending ?? true)
                ? source.OrderByDescending(x => x.PredictedAt).ThenByDescending(x => x.PredictionTicketId)
                : source.OrderBy(x => x.PredictedAt).ThenBy(x => x.PredictionTicketId),
            "confidencescore" => (request.SortDescending ?? true)
                ? source.OrderByDescending(x => x.ConfidenceScore).ThenByDescending(x => x.PredictedAt)
                : source.OrderBy(x => x.ConfidenceScore).ThenBy(x => x.PredictedAt),
            "ticketstatus" => (request.SortDescending ?? true)
                ? source.OrderByDescending(x => x.TicketStatus).ThenByDescending(x => x.PredictedAt)
                : source.OrderBy(x => x.TicketStatus).ThenBy(x => x.PredictedAt),
            "evaluationstatus" => (request.SortDescending ?? true)
                ? source.OrderByDescending(x => x.EvaluationStatus).ThenByDescending(x => x.PredictedAt)
                : source.OrderBy(x => x.EvaluationStatus).ThenBy(x => x.PredictedAt),
            _ => null
        };
}
