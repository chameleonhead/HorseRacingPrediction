using HorseRacingPrediction.Contracts.Owners;
using Refit;

namespace HorseRacingPrediction.ApiClient.Owners;

public interface IOwnersApi
{
    Task<ApiResponse<ExecuteOwnerIdentityRecoveryResponse>> ExecuteOwnerIdentityRecoveryAsync(ExecuteOwnerIdentityRecoveryRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetOwnerResponse>> GetOwnerAsync(GetOwnerRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> MergeOwnerAsync(MergeOwnerRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PreviewOwnerIdentityRecoveryResponse>> PreviewOwnerIdentityRecoveryAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<SearchOwnersResponse>> SearchOwnersAsync(SearchOwnersRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> UpdateOwnerAsync(UpdateOwnerRequest request, CancellationToken cancellationToken = default);
}
