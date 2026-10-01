using Refit;

namespace HorseRacingPrediction.ApiClient.Predictions;

public interface IPredictionsApi
{
    Task<IApiResponse> AddBettingSuggestionAsync(global::HorseRacingPrediction.Contracts.Predictions.AddBettingSuggestionRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> AddPredictionMarkAsync(global::HorseRacingPrediction.Contracts.Predictions.AddPredictionMarkRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> AddPredictionRationaleAsync(global::HorseRacingPrediction.Contracts.Predictions.AddPredictionRationaleRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> CorrectPredictionMetadataAsync(global::HorseRacingPrediction.Contracts.Predictions.CorrectPredictionMetadataRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Predictions.CreatePredictionTicketResponse>> CreatePredictionTicketAsync(global::HorseRacingPrediction.Contracts.Predictions.CreatePredictionTicketRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> EvaluatePredictionTicketAsync(global::HorseRacingPrediction.Contracts.Predictions.EvaluatePredictionTicketRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> FinalizePredictionTicketAsync(global::HorseRacingPrediction.Contracts.Predictions.FinalizePredictionTicketRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Predictions.GetPredictionTicketResponse>> GetPredictionTicketAsync(global::HorseRacingPrediction.Contracts.Predictions.GetPredictionTicketRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> RecalculatePredictionEvaluationAsync(global::HorseRacingPrediction.Contracts.Predictions.RecalculatePredictionEvaluationRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Predictions.SearchPredictionTicketsResponse>> SearchPredictionTicketsAsync(global::HorseRacingPrediction.Contracts.Predictions.SearchPredictionTicketsRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> WithdrawPredictionTicketAsync(global::HorseRacingPrediction.Contracts.Predictions.WithdrawPredictionTicketRequest request, CancellationToken cancellationToken = default);
}
