using Refit;

namespace HorseRacingPrediction.ApiClient.Horses;

internal sealed class HorsesApiFacade(IHorsesTransport transport) : IHorsesApi
{
    public Task<IApiResponse> CorrectHorseDataAsync(global::HorseRacingPrediction.Contracts.Horses.CorrectHorseDataRequest request, CancellationToken cancellationToken = default) => transport.CorrectHorseDataAsync(request.HorseId, request, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Horses.GetHorseParticipationsResponse>> GetHorseParticipationsAsync(global::HorseRacingPrediction.Contracts.Horses.GetHorseParticipationsRequest request, CancellationToken cancellationToken = default) => transport.GetHorseParticipationsAsync(request.HorseId, request.Take, request.Skip, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Horses.GetHorseProfileResponse>> GetHorseProfileAsync(global::HorseRacingPrediction.Contracts.Horses.GetHorseProfileRequest request, CancellationToken cancellationToken = default) => transport.GetHorseProfileAsync(request.HorseId, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Horses.GetHorseRaceHistoryResponse>> GetHorseRaceHistoryAsync(global::HorseRacingPrediction.Contracts.Horses.GetHorseRaceHistoryRequest request, CancellationToken cancellationToken = default) => transport.GetHorseRaceHistoryAsync(request.HorseId, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Horses.GetHorseWeightHistoryResponse>> GetHorseWeightHistoryAsync(global::HorseRacingPrediction.Contracts.Horses.GetHorseWeightHistoryRequest request, CancellationToken cancellationToken = default) => transport.GetHorseWeightHistoryAsync(request.HorseId, cancellationToken);
    public Task<IApiResponse> MergeHorseAliasAsync(global::HorseRacingPrediction.Contracts.Horses.MergeHorseAliasRequest request, CancellationToken cancellationToken = default) => transport.MergeHorseAliasAsync(request.HorseId, request, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Horses.RegisterHorseResponse>> RegisterHorseAsync(global::HorseRacingPrediction.Contracts.Horses.RegisterHorseRequest request, CancellationToken cancellationToken = default) => transport.RegisterHorseAsync(request, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Horses.SearchHorsesResponse>> SearchHorsesAsync(global::HorseRacingPrediction.Contracts.Horses.SearchHorsesRequest request, CancellationToken cancellationToken = default) => transport.SearchHorsesAsync(request.HorseId, request.Query, request.RegisteredName, request.NormalizedName, request.SexCode, request.BirthDateFrom, request.BirthDateTo, request.AliasValue, request.Page, request.PageSize, request.SortBy, request.SortDescending, cancellationToken);
    public Task<IApiResponse> UpdateHorseProfileAsync(global::HorseRacingPrediction.Contracts.Horses.UpdateHorseProfileRequest request, CancellationToken cancellationToken = default) => transport.UpdateHorseProfileAsync(request.HorseId, request, cancellationToken);
}
