using Refit;

namespace HorseRacingPrediction.ApiClient.Identity;

public interface IIdentityApi
{
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Identity.ResolveHorseIdentityResponse>> ResolveHorseIdentityAsync(global::HorseRacingPrediction.Contracts.Identity.ResolveHorseIdentityRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Identity.ResolveRaceIdentityResponse>> ResolveRaceIdentityAsync(global::HorseRacingPrediction.Contracts.Identity.ResolveRaceIdentityRequest request, CancellationToken cancellationToken = default);
}
