using Refit;
using HorseRacingPrediction.Contracts.Identity;

namespace HorseRacingPrediction.ApiClient.Identity;

internal interface IIdentityTransport
{
    [Post("/api/identity/horse")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Identity.ResolveHorseIdentityResponse>> ResolveHorseIdentityAsync([Body] global::HorseRacingPrediction.Contracts.Identity.ResolveHorseIdentityRequest request, CancellationToken cancellationToken);
    [Post("/api/identity/race")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Identity.ResolveRaceIdentityResponse>> ResolveRaceIdentityAsync([Body] global::HorseRacingPrediction.Contracts.Identity.ResolveRaceIdentityRequest request, CancellationToken cancellationToken);
}
