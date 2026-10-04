using HorseRacingPrediction.Contracts.Repairs;
using Refit;

namespace HorseRacingPrediction.ApiClient.Repairs;

internal sealed class RepairsApiFacade(IRepairsTransport transport) : IRepairsApi
{
    public Task<ApiResponse<ApplyHorseIdentityRepairResponse>> ApplyHorseIdentityRepairAsync(ApplyHorseIdentityRepairRequest request, CancellationToken cancellationToken = default) => transport.ApplyHorseIdentityRepairAsync(request, cancellationToken);
    public Task<ApiResponse<ApplySubjectNameNormalizationResponse>> ApplySubjectNameNormalizationAsync(ApplySubjectNameNormalizationRequest request, CancellationToken cancellationToken = default) => transport.ApplySubjectNameNormalizationAsync(request, cancellationToken);
    public Task<ApiResponse<DismissSubjectIdentificationFailuresResponse>> DismissSubjectIdentificationFailuresAsync(DismissSubjectIdentificationFailuresRequest request, CancellationToken cancellationToken = default) => transport.DismissSubjectIdentificationFailuresAsync(request, cancellationToken);
    public Task<ApiResponse<ExecuteSubjectIdentificationRepairResponse>> ExecuteSubjectIdentificationRepairAsync(ExecuteSubjectIdentificationRepairRequest request, CancellationToken cancellationToken = default) => transport.ExecuteSubjectIdentificationRepairAsync(request, cancellationToken);
    public Task<ApiResponse<GetHorseIdentityRepairResponse>> GetHorseIdentityRepairAsync(CancellationToken cancellationToken = default) => transport.GetHorseIdentityRepairAsync(cancellationToken);
    public Task<ApiResponse<GetSubjectIdentificationRepairResponse>> GetSubjectIdentificationRepairAsync(CancellationToken cancellationToken = default) => transport.GetSubjectIdentificationRepairAsync(cancellationToken);
    public Task<ApiResponse<GetSubjectNameNormalizationResponse>> GetSubjectNameNormalizationAsync(GetSubjectNameNormalizationRequest request, CancellationToken cancellationToken = default) => transport.GetSubjectNameNormalizationAsync(request.SubjectType, request.Query, request.Page, request.PageSize, cancellationToken);
}
