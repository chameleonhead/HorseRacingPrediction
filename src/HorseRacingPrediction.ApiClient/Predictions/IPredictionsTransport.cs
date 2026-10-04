using HorseRacingPrediction.Contracts.Predictions;
using Refit;

namespace HorseRacingPrediction.ApiClient.Predictions;

internal interface IPredictionsTransport
{
    [Post("/api/predictions/{predictionTicketId}/betting-suggestions")] Task<IApiResponse> AddBettingSuggestionAsync([AliasAs("predictionTicketId")] string predictionTicketId, [Body] AddBettingSuggestionRequest request, CancellationToken cancellationToken);
    [Post("/api/predictions/{predictionTicketId}/marks")] Task<IApiResponse> AddPredictionMarkAsync([AliasAs("predictionTicketId")] string predictionTicketId, [Body] AddPredictionMarkRequest request, CancellationToken cancellationToken);
    [Post("/api/predictions/{predictionTicketId}/rationales")] Task<IApiResponse> AddPredictionRationaleAsync([AliasAs("predictionTicketId")] string predictionTicketId, [Body] AddPredictionRationaleRequest request, CancellationToken cancellationToken);
    [Patch("/api/predictions/{predictionTicketId}")] Task<IApiResponse> CorrectPredictionMetadataAsync([AliasAs("predictionTicketId")] string predictionTicketId, [Body] CorrectPredictionMetadataRequest request, CancellationToken cancellationToken);
    [Post("/api/predictions")] Task<ApiResponse<CreatePredictionTicketResponse>> CreatePredictionTicketAsync([Body] CreatePredictionTicketRequest request, CancellationToken cancellationToken);
    [Post("/api/predictions/{predictionTicketId}/evaluate")] Task<IApiResponse> EvaluatePredictionTicketAsync([AliasAs("predictionTicketId")] string predictionTicketId, [Body] EvaluatePredictionTicketRequest request, CancellationToken cancellationToken);
    [Post("/api/predictions/{predictionTicketId}/finalize")] Task<IApiResponse> FinalizePredictionTicketAsync([AliasAs("predictionTicketId")] string predictionTicketId, CancellationToken cancellationToken);
    [Get("/api/predictions/{predictionTicketId}")] Task<ApiResponse<GetPredictionTicketResponse>> GetPredictionTicketAsync([AliasAs("predictionTicketId")] string predictionTicketId, CancellationToken cancellationToken);
    [Post("/api/predictions/{predictionTicketId}/recalculate-evaluation")] Task<IApiResponse> RecalculatePredictionEvaluationAsync([AliasAs("predictionTicketId")] string predictionTicketId, [Body] RecalculatePredictionEvaluationRequest request, CancellationToken cancellationToken);
    [Get("/api/predictions")] Task<ApiResponse<SearchPredictionTicketsResponse>> SearchPredictionTicketsAsync([AliasAs("query")] string? query, [AliasAs("predictionTicketId")] string? predictionTicketId, [AliasAs("raceId")] string? raceId, [AliasAs("predictorType")] string? predictorType, [AliasAs("predictorId")] string? predictorId, [AliasAs("ticketStatus")] TicketStatus? ticketStatus, [AliasAs("evaluationStatus")] EvaluationStatus? evaluationStatus, [AliasAs("predictedAtFrom")] DateTimeOffset? predictedAtFrom, [AliasAs("predictedAtTo")] DateTimeOffset? predictedAtTo, [AliasAs("minConfidenceScore")] decimal? minConfidenceScore, [AliasAs("maxConfidenceScore")] decimal? maxConfidenceScore, [AliasAs("summaryComment")] string? summaryComment, [AliasAs("page")] int? page, [AliasAs("pageSize")] int? pageSize, [AliasAs("sortBy")] string? sortBy, [AliasAs("sortDescending")] bool? sortDescending, CancellationToken cancellationToken);
    [Post("/api/predictions/{predictionTicketId}/withdraw")] Task<IApiResponse> WithdrawPredictionTicketAsync([AliasAs("predictionTicketId")] string predictionTicketId, [Body] WithdrawPredictionTicketRequest request, CancellationToken cancellationToken);
}
