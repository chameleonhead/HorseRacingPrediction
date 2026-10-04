using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Repairs;
using Refit;

namespace HorseRacingPrediction.ApiClient.Repairs;

internal interface IRepairsTransport
{
    [Post("/api/admin/repairs/20260913-jra-horse-identity/apply")] Task<ApiResponse<ApplyHorseIdentityRepairResponse>> ApplyHorseIdentityRepairAsync([Body] ApplyHorseIdentityRepairRequest request, CancellationToken cancellationToken);
    [Post("/api/admin/repairs/subject-name-normalization/apply")] Task<ApiResponse<ApplySubjectNameNormalizationResponse>> ApplySubjectNameNormalizationAsync([Body] ApplySubjectNameNormalizationRequest request, CancellationToken cancellationToken);
    [Post("/api/admin/repairs/subject-identification/dismiss")] Task<ApiResponse<DismissSubjectIdentificationFailuresResponse>> DismissSubjectIdentificationFailuresAsync([Body] DismissSubjectIdentificationFailuresRequest request, CancellationToken cancellationToken);
    [Post("/api/admin/repairs/subject-identification/execute")] Task<ApiResponse<ExecuteSubjectIdentificationRepairResponse>> ExecuteSubjectIdentificationRepairAsync([Body] ExecuteSubjectIdentificationRepairRequest request, CancellationToken cancellationToken);
    [Get("/api/admin/repairs/20260913-jra-horse-identity")] Task<ApiResponse<GetHorseIdentityRepairResponse>> GetHorseIdentityRepairAsync(CancellationToken cancellationToken);
    [Get("/api/admin/repairs/subject-identification")] Task<ApiResponse<GetSubjectIdentificationRepairResponse>> GetSubjectIdentificationRepairAsync(CancellationToken cancellationToken);
    [Get("/api/admin/repairs/subject-name-normalization")] Task<ApiResponse<GetSubjectNameNormalizationResponse>> GetSubjectNameNormalizationAsync([AliasAs("subjectType")] CollectionResourceType subjectType, [AliasAs("query")] string? query, [AliasAs("page")] int? page, [AliasAs("pageSize")] int? pageSize, CancellationToken cancellationToken);
}
