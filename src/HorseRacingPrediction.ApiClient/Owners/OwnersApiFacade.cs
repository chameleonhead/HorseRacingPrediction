using HorseRacingPrediction.Contracts.Owners;
using Refit;

namespace HorseRacingPrediction.ApiClient.Owners;

internal sealed class OwnersApiFacade(IOwnersTransport transport) : IOwnersApi
{
    public Task<ApiResponse<ExecuteOwnerIdentityRecoveryResponse>> ExecuteOwnerIdentityRecoveryAsync(ExecuteOwnerIdentityRecoveryRequest request, CancellationToken cancellationToken = default) => transport.ExecuteOwnerIdentityRecoveryAsync(request, cancellationToken);
    public Task<ApiResponse<GetOwnerResponse>> GetOwnerAsync(GetOwnerRequest request, CancellationToken cancellationToken = default) => transport.GetOwnerAsync(request.OwnerId, request.Take, request.Skip, cancellationToken);
    public Task<IApiResponse> MergeOwnerAsync(MergeOwnerRequest request, CancellationToken cancellationToken = default) => transport.MergeOwnerAsync(request.OwnerId, request, cancellationToken);
    public Task<ApiResponse<PreviewOwnerIdentityRecoveryResponse>> PreviewOwnerIdentityRecoveryAsync(CancellationToken cancellationToken = default) => transport.PreviewOwnerIdentityRecoveryAsync(cancellationToken);
    public Task<ApiResponse<SearchOwnersResponse>> SearchOwnersAsync(SearchOwnersRequest request, CancellationToken cancellationToken = default) => transport.SearchOwnersAsync(request.Query, cancellationToken);
    public Task<IApiResponse> UpdateOwnerAsync(UpdateOwnerRequest request, CancellationToken cancellationToken = default) => transport.UpdateOwnerAsync(request.OwnerId, request, cancellationToken);
}
