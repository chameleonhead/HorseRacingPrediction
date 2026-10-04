using HorseRacingPrediction.Contracts.Predictions;
using Refit;

namespace HorseRacingPrediction.ApiClient.Predictions;

internal sealed class PredictionsApiFacade(IPredictionsTransport transport) : IPredictionsApi
{
    public Task<IApiResponse> AddBettingSuggestionAsync(AddBettingSuggestionRequest request, CancellationToken cancellationToken = default) => transport.AddBettingSuggestionAsync(request.PredictionTicketId, request, cancellationToken);
    public Task<IApiResponse> AddPredictionMarkAsync(AddPredictionMarkRequest request, CancellationToken cancellationToken = default) => transport.AddPredictionMarkAsync(request.PredictionTicketId, request, cancellationToken);
    public Task<IApiResponse> AddPredictionRationaleAsync(AddPredictionRationaleRequest request, CancellationToken cancellationToken = default) => transport.AddPredictionRationaleAsync(request.PredictionTicketId, request, cancellationToken);
    public Task<IApiResponse> CorrectPredictionMetadataAsync(CorrectPredictionMetadataRequest request, CancellationToken cancellationToken = default) => transport.CorrectPredictionMetadataAsync(request.PredictionTicketId, request, cancellationToken);
    public Task<ApiResponse<CreatePredictionTicketResponse>> CreatePredictionTicketAsync(CreatePredictionTicketRequest request, CancellationToken cancellationToken = default) => transport.CreatePredictionTicketAsync(request, cancellationToken);
    public Task<IApiResponse> EvaluatePredictionTicketAsync(EvaluatePredictionTicketRequest request, CancellationToken cancellationToken = default) => transport.EvaluatePredictionTicketAsync(request.PredictionTicketId, request, cancellationToken);
    public Task<IApiResponse> FinalizePredictionTicketAsync(FinalizePredictionTicketRequest request, CancellationToken cancellationToken = default) => transport.FinalizePredictionTicketAsync(request.PredictionTicketId, cancellationToken);
    public Task<ApiResponse<GetPredictionTicketResponse>> GetPredictionTicketAsync(GetPredictionTicketRequest request, CancellationToken cancellationToken = default) => transport.GetPredictionTicketAsync(request.PredictionTicketId, cancellationToken);
    public Task<IApiResponse> RecalculatePredictionEvaluationAsync(RecalculatePredictionEvaluationRequest request, CancellationToken cancellationToken = default) => transport.RecalculatePredictionEvaluationAsync(request.PredictionTicketId, request, cancellationToken);
    public Task<ApiResponse<SearchPredictionTicketsResponse>> SearchPredictionTicketsAsync(SearchPredictionTicketsRequest request, CancellationToken cancellationToken = default) => transport.SearchPredictionTicketsAsync(request.Query, request.PredictionTicketId, request.RaceId, request.PredictorType, request.PredictorId, request.TicketStatus, request.EvaluationStatus, request.PredictedAtFrom, request.PredictedAtTo, request.MinConfidenceScore, request.MaxConfidenceScore, request.SummaryComment, request.Page, request.PageSize, request.SortBy, request.SortDescending, cancellationToken);
    public Task<IApiResponse> WithdrawPredictionTicketAsync(WithdrawPredictionTicketRequest request, CancellationToken cancellationToken = default) => transport.WithdrawPredictionTicketAsync(request.PredictionTicketId, request, cancellationToken);
}
