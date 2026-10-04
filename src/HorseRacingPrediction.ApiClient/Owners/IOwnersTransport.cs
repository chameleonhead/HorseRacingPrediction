using HorseRacingPrediction.Contracts.Owners;
using Refit;

namespace HorseRacingPrediction.ApiClient.Owners;

internal interface IOwnersTransport
{
    [Post("/api/admin/repairs/owner-identity/execute")] Task<ApiResponse<ExecuteOwnerIdentityRecoveryResponse>> ExecuteOwnerIdentityRecoveryAsync([Body] ExecuteOwnerIdentityRecoveryRequest request, CancellationToken cancellationToken);
    [Get("/api/owners/{ownerId}")] Task<ApiResponse<GetOwnerResponse>> GetOwnerAsync([AliasAs("ownerId")] string ownerId, [AliasAs("take")] int? take, [AliasAs("skip")] int? skip, CancellationToken cancellationToken);
    [Post("/api/owners/{ownerId}/merge")] Task<IApiResponse> MergeOwnerAsync([AliasAs("ownerId")] string ownerId, [Body] MergeOwnerRequest request, CancellationToken cancellationToken);
    [Get("/api/admin/repairs/owner-identity")] Task<ApiResponse<PreviewOwnerIdentityRecoveryResponse>> PreviewOwnerIdentityRecoveryAsync(CancellationToken cancellationToken);
    [Get("/api/owners")] Task<ApiResponse<SearchOwnersResponse>> SearchOwnersAsync([AliasAs("query")] string? query, CancellationToken cancellationToken);
    [Put("/api/owners/{ownerId}")] Task<IApiResponse> UpdateOwnerAsync([AliasAs("ownerId")] string ownerId, [Body] UpdateOwnerRequest request, CancellationToken cancellationToken);
}
