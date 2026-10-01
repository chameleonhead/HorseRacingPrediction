using Refit;

namespace HorseRacingPrediction.ApiClient.PredictionScheduling;

internal sealed class PredictionSchedulingApiFacade(IPredictionSchedulingTransport transport) : IPredictionSchedulingApi
{
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.PredictionScheduling.AcquirePredictionCandidateLeasesResponse>> AcquirePredictionCandidateLeasesAsync(global::HorseRacingPrediction.Contracts.PredictionScheduling.AcquirePredictionCandidateLeasesRequest request, CancellationToken cancellationToken = default) => transport.AcquirePredictionCandidateLeasesAsync(request, cancellationToken);
    public Task<IApiResponse> EnqueuePredictionCandidatesAsync(global::HorseRacingPrediction.Contracts.PredictionScheduling.EnqueuePredictionCandidatesRequest request, CancellationToken cancellationToken = default) => transport.EnqueuePredictionCandidatesAsync(request, cancellationToken);
    public Task<IApiResponse> TransitionPredictionCandidateAsync(global::HorseRacingPrediction.Contracts.PredictionScheduling.TransitionPredictionCandidateRequest request, CancellationToken cancellationToken = default) => transport.TransitionPredictionCandidateAsync(request.RaceId, request, cancellationToken);
}
