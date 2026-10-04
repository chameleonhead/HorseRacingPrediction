using HorseRacingPrediction.Contracts.Horses;
using Refit;

namespace HorseRacingPrediction.ApiClient.Horses;

public interface IHorsesApi
{
    Task<IApiResponse> CorrectHorseDataAsync(CorrectHorseDataRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetHorseParticipationsResponse>> GetHorseParticipationsAsync(GetHorseParticipationsRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetHorseProfileResponse>> GetHorseProfileAsync(GetHorseProfileRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetHorseRaceHistoryResponse>> GetHorseRaceHistoryAsync(GetHorseRaceHistoryRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetHorseWeightHistoryResponse>> GetHorseWeightHistoryAsync(GetHorseWeightHistoryRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> MergeHorseAliasAsync(MergeHorseAliasRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<RegisterHorseResponse>> RegisterHorseAsync(RegisterHorseRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<SearchHorsesResponse>> SearchHorsesAsync(SearchHorsesRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> UpdateHorseProfileAsync(UpdateHorseProfileRequest request, CancellationToken cancellationToken = default);
}
