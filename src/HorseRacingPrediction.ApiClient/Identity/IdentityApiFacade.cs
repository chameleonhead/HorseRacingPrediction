using Refit;

namespace HorseRacingPrediction.ApiClient.Identity;

internal sealed class IdentityApiFacade(IIdentityTransport transport) : IIdentityApi
{
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Identity.ResolveHorseIdentityResponse>> ResolveHorseIdentityAsync(global::HorseRacingPrediction.Contracts.Identity.ResolveHorseIdentityRequest request, CancellationToken cancellationToken = default) => transport.ResolveHorseIdentityAsync(request, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Identity.ResolveRaceIdentityResponse>> ResolveRaceIdentityAsync(global::HorseRacingPrediction.Contracts.Identity.ResolveRaceIdentityRequest request, CancellationToken cancellationToken = default) => transport.ResolveRaceIdentityAsync(request, cancellationToken);
}
