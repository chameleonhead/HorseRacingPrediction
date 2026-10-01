using Refit;

namespace HorseRacingPrediction.ApiClient.PredictionScheduling;

public interface IPredictionSchedulingApi
{
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.PredictionScheduling.AcquirePredictionCandidateLeasesResponse>> AcquirePredictionCandidateLeasesAsync(global::HorseRacingPrediction.Contracts.PredictionScheduling.AcquirePredictionCandidateLeasesRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> EnqueuePredictionCandidatesAsync(global::HorseRacingPrediction.Contracts.PredictionScheduling.EnqueuePredictionCandidatesRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> TransitionPredictionCandidateAsync(global::HorseRacingPrediction.Contracts.PredictionScheduling.TransitionPredictionCandidateRequest request, CancellationToken cancellationToken = default);
}
