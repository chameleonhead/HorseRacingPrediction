using Refit;

namespace HorseRacingPrediction.ApiClient.Owners;

internal sealed class OwnersApiFacade(IOwnersTransport transport) : IOwnersApi
{
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Owners.ExecuteOwnerIdentityRecoveryResponse>> ExecuteOwnerIdentityRecoveryAsync(global::HorseRacingPrediction.Contracts.Owners.ExecuteOwnerIdentityRecoveryRequest request, CancellationToken cancellationToken = default) => transport.ExecuteOwnerIdentityRecoveryAsync(request, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Owners.GetOwnerResponse>> GetOwnerAsync(global::HorseRacingPrediction.Contracts.Owners.GetOwnerRequest request, CancellationToken cancellationToken = default) => transport.GetOwnerAsync(request.OwnerId, request.Take, request.Skip, cancellationToken);
    public Task<IApiResponse> MergeOwnerAsync(global::HorseRacingPrediction.Contracts.Owners.MergeOwnerRequest request, CancellationToken cancellationToken = default) => transport.MergeOwnerAsync(request.OwnerId, request, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Owners.PreviewOwnerIdentityRecoveryResponse>> PreviewOwnerIdentityRecoveryAsync(CancellationToken cancellationToken = default) => transport.PreviewOwnerIdentityRecoveryAsync(cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Owners.SearchOwnersResponse>> SearchOwnersAsync(global::HorseRacingPrediction.Contracts.Owners.SearchOwnersRequest request, CancellationToken cancellationToken = default) => transport.SearchOwnersAsync(request.Query, cancellationToken);
    public Task<IApiResponse> UpdateOwnerAsync(global::HorseRacingPrediction.Contracts.Owners.UpdateOwnerRequest request, CancellationToken cancellationToken = default) => transport.UpdateOwnerAsync(request.OwnerId, request, cancellationToken);
}
