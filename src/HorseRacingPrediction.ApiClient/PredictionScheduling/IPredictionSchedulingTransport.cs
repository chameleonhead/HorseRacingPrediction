using Refit;
using HorseRacingPrediction.Contracts.PredictionScheduling;

namespace HorseRacingPrediction.ApiClient.PredictionScheduling;

internal interface IPredictionSchedulingTransport
{
    [Post("/api/v2/internal/prediction-candidate-leases")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.PredictionScheduling.AcquirePredictionCandidateLeasesResponse>> AcquirePredictionCandidateLeasesAsync([Body] global::HorseRacingPrediction.Contracts.PredictionScheduling.AcquirePredictionCandidateLeasesRequest request, CancellationToken cancellationToken);
    [Post("/api/v2/internal/prediction-candidates")] Task<IApiResponse> EnqueuePredictionCandidatesAsync([Body] global::HorseRacingPrediction.Contracts.PredictionScheduling.EnqueuePredictionCandidatesRequest request, CancellationToken cancellationToken);
    [Patch("/api/v2/internal/prediction-candidates/{raceId}")] Task<IApiResponse> TransitionPredictionCandidateAsync([AliasAs("raceId")] string raceId, [Body] global::HorseRacingPrediction.Contracts.PredictionScheduling.TransitionPredictionCandidateRequest request, CancellationToken cancellationToken);
}
