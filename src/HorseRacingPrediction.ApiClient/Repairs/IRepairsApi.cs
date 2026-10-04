using HorseRacingPrediction.Contracts.Repairs;
using Refit;

namespace HorseRacingPrediction.ApiClient.Repairs;

public interface IRepairsApi
{
    Task<ApiResponse<ApplyHorseIdentityRepairResponse>> ApplyHorseIdentityRepairAsync(ApplyHorseIdentityRepairRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ApplySubjectNameNormalizationResponse>> ApplySubjectNameNormalizationAsync(ApplySubjectNameNormalizationRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<DismissSubjectIdentificationFailuresResponse>> DismissSubjectIdentificationFailuresAsync(DismissSubjectIdentificationFailuresRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ExecuteSubjectIdentificationRepairResponse>> ExecuteSubjectIdentificationRepairAsync(ExecuteSubjectIdentificationRepairRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetHorseIdentityRepairResponse>> GetHorseIdentityRepairAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<GetSubjectIdentificationRepairResponse>> GetSubjectIdentificationRepairAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<GetSubjectNameNormalizationResponse>> GetSubjectNameNormalizationAsync(GetSubjectNameNormalizationRequest request, CancellationToken cancellationToken = default);
}
