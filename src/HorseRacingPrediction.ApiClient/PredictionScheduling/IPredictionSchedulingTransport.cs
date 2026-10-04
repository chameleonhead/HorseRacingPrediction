using HorseRacingPrediction.Contracts.PredictionScheduling;
using Refit;

namespace HorseRacingPrediction.ApiClient.PredictionScheduling;

internal interface IPredictionSchedulingTransport
{
    [Post("/api/v2/internal/prediction-candidate-leases")] Task<ApiResponse<AcquirePredictionCandidateLeasesResponse>> AcquirePredictionCandidateLeasesAsync([Body] AcquirePredictionCandidateLeasesRequest request, CancellationToken cancellationToken);
    [Post("/api/v2/internal/prediction-candidates")] Task<IApiResponse> EnqueuePredictionCandidatesAsync([Body] EnqueuePredictionCandidatesRequest request, CancellationToken cancellationToken);
    [Patch("/api/v2/internal/prediction-candidates/{raceId}")] Task<IApiResponse> TransitionPredictionCandidateAsync([AliasAs("raceId")] string raceId, [Body] TransitionPredictionCandidateRequest request, CancellationToken cancellationToken);
}
