using HorseRacingPrediction.Contracts.Identity;
using Refit;

namespace HorseRacingPrediction.ApiClient.Identity;

public interface IIdentityApi
{
    Task<ApiResponse<ResolveHorseIdentityResponse>> ResolveHorseIdentityAsync(ResolveHorseIdentityRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ResolveRaceIdentityResponse>> ResolveRaceIdentityAsync(ResolveRaceIdentityRequest request, CancellationToken cancellationToken = default);
}
