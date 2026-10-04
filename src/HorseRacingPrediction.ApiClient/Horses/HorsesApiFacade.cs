using HorseRacingPrediction.Contracts.Horses;
using Refit;

namespace HorseRacingPrediction.ApiClient.Horses;

internal sealed class HorsesApiFacade(IHorsesTransport transport) : IHorsesApi
{
    public Task<IApiResponse> CorrectHorseDataAsync(CorrectHorseDataRequest request, CancellationToken cancellationToken = default) => transport.CorrectHorseDataAsync(request.HorseId, request, cancellationToken);
    public Task<ApiResponse<GetHorseParticipationsResponse>> GetHorseParticipationsAsync(GetHorseParticipationsRequest request, CancellationToken cancellationToken = default) => transport.GetHorseParticipationsAsync(request.HorseId, request.Take, request.Skip, cancellationToken);
    public Task<ApiResponse<GetHorseProfileResponse>> GetHorseProfileAsync(GetHorseProfileRequest request, CancellationToken cancellationToken = default) => transport.GetHorseProfileAsync(request.HorseId, cancellationToken);
    public Task<ApiResponse<GetHorseRaceHistoryResponse>> GetHorseRaceHistoryAsync(GetHorseRaceHistoryRequest request, CancellationToken cancellationToken = default) => transport.GetHorseRaceHistoryAsync(request.HorseId, cancellationToken);
    public Task<ApiResponse<GetHorseWeightHistoryResponse>> GetHorseWeightHistoryAsync(GetHorseWeightHistoryRequest request, CancellationToken cancellationToken = default) => transport.GetHorseWeightHistoryAsync(request.HorseId, cancellationToken);
    public Task<IApiResponse> MergeHorseAliasAsync(MergeHorseAliasRequest request, CancellationToken cancellationToken = default) => transport.MergeHorseAliasAsync(request.HorseId, request, cancellationToken);
    public Task<ApiResponse<RegisterHorseResponse>> RegisterHorseAsync(RegisterHorseRequest request, CancellationToken cancellationToken = default) => transport.RegisterHorseAsync(request, cancellationToken);
    public Task<ApiResponse<SearchHorsesResponse>> SearchHorsesAsync(SearchHorsesRequest request, CancellationToken cancellationToken = default) => transport.SearchHorsesAsync(request.HorseId, request.Query, request.RegisteredName, request.NormalizedName, request.SexCode, request.BirthDateFrom, request.BirthDateTo, request.AliasValue, request.Page, request.PageSize, request.SortBy, request.SortDescending, cancellationToken);
    public Task<IApiResponse> UpdateHorseProfileAsync(UpdateHorseProfileRequest request, CancellationToken cancellationToken = default) => transport.UpdateHorseProfileAsync(request.HorseId, request, cancellationToken);
}
