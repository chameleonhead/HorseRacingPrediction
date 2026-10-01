using Refit;

namespace HorseRacingPrediction.ApiClient.Repairs;

internal sealed class RepairsApiFacade(IRepairsTransport transport) : IRepairsApi
{
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Repairs.ApplyHorseIdentityRepairResponse>> ApplyHorseIdentityRepairAsync(global::HorseRacingPrediction.Contracts.Repairs.ApplyHorseIdentityRepairRequest request, CancellationToken cancellationToken = default) => transport.ApplyHorseIdentityRepairAsync(request, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Repairs.ApplySubjectNameNormalizationResponse>> ApplySubjectNameNormalizationAsync(global::HorseRacingPrediction.Contracts.Repairs.ApplySubjectNameNormalizationRequest request, CancellationToken cancellationToken = default) => transport.ApplySubjectNameNormalizationAsync(request, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Repairs.DismissSubjectIdentificationFailuresResponse>> DismissSubjectIdentificationFailuresAsync(global::HorseRacingPrediction.Contracts.Repairs.DismissSubjectIdentificationFailuresRequest request, CancellationToken cancellationToken = default) => transport.DismissSubjectIdentificationFailuresAsync(request, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Repairs.ExecuteSubjectIdentificationRepairResponse>> ExecuteSubjectIdentificationRepairAsync(global::HorseRacingPrediction.Contracts.Repairs.ExecuteSubjectIdentificationRepairRequest request, CancellationToken cancellationToken = default) => transport.ExecuteSubjectIdentificationRepairAsync(request, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Repairs.GetHorseIdentityRepairResponse>> GetHorseIdentityRepairAsync(CancellationToken cancellationToken = default) => transport.GetHorseIdentityRepairAsync(cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Repairs.GetSubjectIdentificationRepairResponse>> GetSubjectIdentificationRepairAsync(CancellationToken cancellationToken = default) => transport.GetSubjectIdentificationRepairAsync(cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Repairs.GetSubjectNameNormalizationResponse>> GetSubjectNameNormalizationAsync(global::HorseRacingPrediction.Contracts.Repairs.GetSubjectNameNormalizationRequest request, CancellationToken cancellationToken = default) => transport.GetSubjectNameNormalizationAsync(request.SubjectType, request.Query, request.Page, request.PageSize, cancellationToken);
}
