using Refit;
using HorseRacingPrediction.Contracts.Repairs;
using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.ApiClient.Repairs;

internal interface IRepairsTransport
{
    [Post("/api/admin/repairs/20260913-jra-horse-identity/apply")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Repairs.ApplyHorseIdentityRepairResponse>> ApplyHorseIdentityRepairAsync([Body] global::HorseRacingPrediction.Contracts.Repairs.ApplyHorseIdentityRepairRequest request, CancellationToken cancellationToken);
    [Post("/api/admin/repairs/subject-name-normalization/apply")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Repairs.ApplySubjectNameNormalizationResponse>> ApplySubjectNameNormalizationAsync([Body] global::HorseRacingPrediction.Contracts.Repairs.ApplySubjectNameNormalizationRequest request, CancellationToken cancellationToken);
    [Post("/api/admin/repairs/subject-identification/dismiss")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Repairs.DismissSubjectIdentificationFailuresResponse>> DismissSubjectIdentificationFailuresAsync([Body] global::HorseRacingPrediction.Contracts.Repairs.DismissSubjectIdentificationFailuresRequest request, CancellationToken cancellationToken);
    [Post("/api/admin/repairs/subject-identification/execute")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Repairs.ExecuteSubjectIdentificationRepairResponse>> ExecuteSubjectIdentificationRepairAsync([Body] global::HorseRacingPrediction.Contracts.Repairs.ExecuteSubjectIdentificationRepairRequest request, CancellationToken cancellationToken);
    [Get("/api/admin/repairs/20260913-jra-horse-identity")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Repairs.GetHorseIdentityRepairResponse>> GetHorseIdentityRepairAsync(CancellationToken cancellationToken);
    [Get("/api/admin/repairs/subject-identification")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Repairs.GetSubjectIdentificationRepairResponse>> GetSubjectIdentificationRepairAsync(CancellationToken cancellationToken);
    [Get("/api/admin/repairs/subject-name-normalization")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Repairs.GetSubjectNameNormalizationResponse>> GetSubjectNameNormalizationAsync([AliasAs("subjectType")] global::HorseRacingPrediction.Contracts.Collection.CollectionResourceType subjectType, [AliasAs("query")] string? query, [AliasAs("page")] int? page, [AliasAs("pageSize")] int? pageSize, CancellationToken cancellationToken);
}
