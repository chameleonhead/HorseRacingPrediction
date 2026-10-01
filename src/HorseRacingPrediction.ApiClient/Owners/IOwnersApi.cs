using Refit;

namespace HorseRacingPrediction.ApiClient.Owners;

public interface IOwnersApi
{
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Owners.ExecuteOwnerIdentityRecoveryResponse>> ExecuteOwnerIdentityRecoveryAsync(global::HorseRacingPrediction.Contracts.Owners.ExecuteOwnerIdentityRecoveryRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Owners.GetOwnerResponse>> GetOwnerAsync(global::HorseRacingPrediction.Contracts.Owners.GetOwnerRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> MergeOwnerAsync(global::HorseRacingPrediction.Contracts.Owners.MergeOwnerRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Owners.PreviewOwnerIdentityRecoveryResponse>> PreviewOwnerIdentityRecoveryAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Owners.SearchOwnersResponse>> SearchOwnersAsync(global::HorseRacingPrediction.Contracts.Owners.SearchOwnersRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> UpdateOwnerAsync(global::HorseRacingPrediction.Contracts.Owners.UpdateOwnerRequest request, CancellationToken cancellationToken = default);
}
