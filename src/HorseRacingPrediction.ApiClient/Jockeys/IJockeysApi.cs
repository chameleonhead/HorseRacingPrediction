using HorseRacingPrediction.Contracts.Jockeys;
using Refit;

namespace HorseRacingPrediction.ApiClient.Jockeys;

public interface IJockeysApi
{
    Task<IApiResponse> CorrectJockeyDataAsync(CorrectJockeyDataRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetJockeyParticipationsResponse>> GetJockeyParticipationsAsync(GetJockeyParticipationsRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetJockeyProfileResponse>> GetJockeyProfileAsync(GetJockeyProfileRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetJockeyRaceHistoryResponse>> GetJockeyRaceHistoryAsync(GetJockeyRaceHistoryRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> MergeJockeyAliasAsync(MergeJockeyAliasRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<RegisterJockeyResponse>> RegisterJockeyAsync(RegisterJockeyRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<SearchJockeysResponse>> SearchJockeysAsync(SearchJockeysRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> UpdateJockeyProfileAsync(UpdateJockeyProfileRequest request, CancellationToken cancellationToken = default);
}
