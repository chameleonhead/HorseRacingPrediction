using Refit;
using HorseRacingPrediction.Contracts.Owners;

namespace HorseRacingPrediction.ApiClient.Owners;

internal interface IOwnersTransport
{
    [Post("/api/admin/repairs/owner-identity/execute")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Owners.ExecuteOwnerIdentityRecoveryResponse>> ExecuteOwnerIdentityRecoveryAsync([Body] global::HorseRacingPrediction.Contracts.Owners.ExecuteOwnerIdentityRecoveryRequest request, CancellationToken cancellationToken);
    [Get("/api/owners/{ownerId}")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Owners.GetOwnerResponse>> GetOwnerAsync([AliasAs("ownerId")] string ownerId, [AliasAs("take")] int? take, [AliasAs("skip")] int? skip, CancellationToken cancellationToken);
    [Post("/api/owners/{ownerId}/merge")] Task<IApiResponse> MergeOwnerAsync([AliasAs("ownerId")] string ownerId, [Body] global::HorseRacingPrediction.Contracts.Owners.MergeOwnerRequest request, CancellationToken cancellationToken);
    [Get("/api/admin/repairs/owner-identity")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Owners.PreviewOwnerIdentityRecoveryResponse>> PreviewOwnerIdentityRecoveryAsync(CancellationToken cancellationToken);
    [Get("/api/owners")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Owners.SearchOwnersResponse>> SearchOwnersAsync([AliasAs("query")] string? query, CancellationToken cancellationToken);
    [Put("/api/owners/{ownerId}")] Task<IApiResponse> UpdateOwnerAsync([AliasAs("ownerId")] string ownerId, [Body] global::HorseRacingPrediction.Contracts.Owners.UpdateOwnerRequest request, CancellationToken cancellationToken);
}
