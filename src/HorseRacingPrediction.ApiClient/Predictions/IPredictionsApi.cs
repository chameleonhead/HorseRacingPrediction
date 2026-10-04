using HorseRacingPrediction.Contracts.Predictions;
using Refit;

namespace HorseRacingPrediction.ApiClient.Predictions;

public interface IPredictionsApi
{
    Task<IApiResponse> AddBettingSuggestionAsync(AddBettingSuggestionRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> AddPredictionMarkAsync(AddPredictionMarkRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> AddPredictionRationaleAsync(AddPredictionRationaleRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> CorrectPredictionMetadataAsync(CorrectPredictionMetadataRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<CreatePredictionTicketResponse>> CreatePredictionTicketAsync(CreatePredictionTicketRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> EvaluatePredictionTicketAsync(EvaluatePredictionTicketRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> FinalizePredictionTicketAsync(FinalizePredictionTicketRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetPredictionTicketResponse>> GetPredictionTicketAsync(GetPredictionTicketRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> RecalculatePredictionEvaluationAsync(RecalculatePredictionEvaluationRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<SearchPredictionTicketsResponse>> SearchPredictionTicketsAsync(SearchPredictionTicketsRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> WithdrawPredictionTicketAsync(WithdrawPredictionTicketRequest request, CancellationToken cancellationToken = default);
}
