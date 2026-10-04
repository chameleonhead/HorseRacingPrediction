using HorseRacingPrediction.Contracts.Identity;
using Refit;

namespace HorseRacingPrediction.ApiClient.Identity;

internal interface IIdentityTransport
{
    [Post("/api/identity/horse")] Task<ApiResponse<ResolveHorseIdentityResponse>> ResolveHorseIdentityAsync([Body] ResolveHorseIdentityRequest request, CancellationToken cancellationToken);
    [Post("/api/identity/race")] Task<ApiResponse<ResolveRaceIdentityResponse>> ResolveRaceIdentityAsync([Body] ResolveRaceIdentityRequest request, CancellationToken cancellationToken);
}
