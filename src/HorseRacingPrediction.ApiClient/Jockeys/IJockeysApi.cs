using Refit;

namespace HorseRacingPrediction.ApiClient.Jockeys;

public interface IJockeysApi
{
    Task<IApiResponse> CorrectJockeyDataAsync(global::HorseRacingPrediction.Contracts.Jockeys.CorrectJockeyDataRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Jockeys.GetJockeyParticipationsResponse>> GetJockeyParticipationsAsync(global::HorseRacingPrediction.Contracts.Jockeys.GetJockeyParticipationsRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Jockeys.GetJockeyProfileResponse>> GetJockeyProfileAsync(global::HorseRacingPrediction.Contracts.Jockeys.GetJockeyProfileRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Jockeys.GetJockeyRaceHistoryResponse>> GetJockeyRaceHistoryAsync(global::HorseRacingPrediction.Contracts.Jockeys.GetJockeyRaceHistoryRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> MergeJockeyAliasAsync(global::HorseRacingPrediction.Contracts.Jockeys.MergeJockeyAliasRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Jockeys.RegisterJockeyResponse>> RegisterJockeyAsync(global::HorseRacingPrediction.Contracts.Jockeys.RegisterJockeyRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Jockeys.SearchJockeysResponse>> SearchJockeysAsync(global::HorseRacingPrediction.Contracts.Jockeys.SearchJockeysRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> UpdateJockeyProfileAsync(global::HorseRacingPrediction.Contracts.Jockeys.UpdateJockeyProfileRequest request, CancellationToken cancellationToken = default);
}
