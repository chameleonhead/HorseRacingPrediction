using HorseRacingPrediction.Contracts.Identity;
using Refit;

namespace HorseRacingPrediction.ApiClient.Identity;

internal sealed class IdentityApiFacade(IIdentityTransport transport) : IIdentityApi
{
    public Task<ApiResponse<ResolveHorseIdentityResponse>> ResolveHorseIdentityAsync(ResolveHorseIdentityRequest request, CancellationToken cancellationToken = default) => transport.ResolveHorseIdentityAsync(request, cancellationToken);
    public Task<ApiResponse<ResolveRaceIdentityResponse>> ResolveRaceIdentityAsync(ResolveRaceIdentityRequest request, CancellationToken cancellationToken = default) => transport.ResolveRaceIdentityAsync(request, cancellationToken);
}
