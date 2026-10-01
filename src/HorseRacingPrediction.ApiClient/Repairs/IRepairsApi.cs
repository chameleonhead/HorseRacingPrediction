using Refit;

namespace HorseRacingPrediction.ApiClient.Repairs;

public interface IRepairsApi
{
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Repairs.ApplyHorseIdentityRepairResponse>> ApplyHorseIdentityRepairAsync(global::HorseRacingPrediction.Contracts.Repairs.ApplyHorseIdentityRepairRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Repairs.ApplySubjectNameNormalizationResponse>> ApplySubjectNameNormalizationAsync(global::HorseRacingPrediction.Contracts.Repairs.ApplySubjectNameNormalizationRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Repairs.DismissSubjectIdentificationFailuresResponse>> DismissSubjectIdentificationFailuresAsync(global::HorseRacingPrediction.Contracts.Repairs.DismissSubjectIdentificationFailuresRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Repairs.ExecuteSubjectIdentificationRepairResponse>> ExecuteSubjectIdentificationRepairAsync(global::HorseRacingPrediction.Contracts.Repairs.ExecuteSubjectIdentificationRepairRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Repairs.GetHorseIdentityRepairResponse>> GetHorseIdentityRepairAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Repairs.GetSubjectIdentificationRepairResponse>> GetSubjectIdentificationRepairAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Repairs.GetSubjectNameNormalizationResponse>> GetSubjectNameNormalizationAsync(global::HorseRacingPrediction.Contracts.Repairs.GetSubjectNameNormalizationRequest request, CancellationToken cancellationToken = default);
}
