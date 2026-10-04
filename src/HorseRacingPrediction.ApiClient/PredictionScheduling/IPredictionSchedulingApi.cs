using HorseRacingPrediction.Contracts.PredictionScheduling;
using Refit;

namespace HorseRacingPrediction.ApiClient.PredictionScheduling;

public interface IPredictionSchedulingApi
{
    Task<ApiResponse<AcquirePredictionCandidateLeasesResponse>> AcquirePredictionCandidateLeasesAsync(AcquirePredictionCandidateLeasesRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> EnqueuePredictionCandidatesAsync(EnqueuePredictionCandidatesRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> TransitionPredictionCandidateAsync(TransitionPredictionCandidateRequest request, CancellationToken cancellationToken = default);
}
