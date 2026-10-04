using HorseRacingPrediction.Contracts.PredictionScheduling;
using Refit;

namespace HorseRacingPrediction.ApiClient.PredictionScheduling;

internal sealed class PredictionSchedulingApiFacade(IPredictionSchedulingTransport transport) : IPredictionSchedulingApi
{
    public Task<ApiResponse<AcquirePredictionCandidateLeasesResponse>> AcquirePredictionCandidateLeasesAsync(AcquirePredictionCandidateLeasesRequest request, CancellationToken cancellationToken = default) => transport.AcquirePredictionCandidateLeasesAsync(request, cancellationToken);
    public Task<IApiResponse> EnqueuePredictionCandidatesAsync(EnqueuePredictionCandidatesRequest request, CancellationToken cancellationToken = default) => transport.EnqueuePredictionCandidatesAsync(request, cancellationToken);
    public Task<IApiResponse> TransitionPredictionCandidateAsync(TransitionPredictionCandidateRequest request, CancellationToken cancellationToken = default) => transport.TransitionPredictionCandidateAsync(request.RaceId, request, cancellationToken);
}
