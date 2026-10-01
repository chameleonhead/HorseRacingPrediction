using Refit;

namespace HorseRacingPrediction.ApiClient.Jockeys;

internal sealed class JockeysApiFacade(IJockeysTransport transport) : IJockeysApi
{
    public Task<IApiResponse> CorrectJockeyDataAsync(global::HorseRacingPrediction.Contracts.Jockeys.CorrectJockeyDataRequest request, CancellationToken cancellationToken = default) => transport.CorrectJockeyDataAsync(request.JockeyId, request, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Jockeys.GetJockeyParticipationsResponse>> GetJockeyParticipationsAsync(global::HorseRacingPrediction.Contracts.Jockeys.GetJockeyParticipationsRequest request, CancellationToken cancellationToken = default) => transport.GetJockeyParticipationsAsync(request.JockeyId, request.Take, request.Skip, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Jockeys.GetJockeyProfileResponse>> GetJockeyProfileAsync(global::HorseRacingPrediction.Contracts.Jockeys.GetJockeyProfileRequest request, CancellationToken cancellationToken = default) => transport.GetJockeyProfileAsync(request.JockeyId, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Jockeys.GetJockeyRaceHistoryResponse>> GetJockeyRaceHistoryAsync(global::HorseRacingPrediction.Contracts.Jockeys.GetJockeyRaceHistoryRequest request, CancellationToken cancellationToken = default) => transport.GetJockeyRaceHistoryAsync(request.JockeyId, cancellationToken);
    public Task<IApiResponse> MergeJockeyAliasAsync(global::HorseRacingPrediction.Contracts.Jockeys.MergeJockeyAliasRequest request, CancellationToken cancellationToken = default) => transport.MergeJockeyAliasAsync(request.JockeyId, request, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Jockeys.RegisterJockeyResponse>> RegisterJockeyAsync(global::HorseRacingPrediction.Contracts.Jockeys.RegisterJockeyRequest request, CancellationToken cancellationToken = default) => transport.RegisterJockeyAsync(request, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Jockeys.SearchJockeysResponse>> SearchJockeysAsync(global::HorseRacingPrediction.Contracts.Jockeys.SearchJockeysRequest request, CancellationToken cancellationToken = default) => transport.SearchJockeysAsync(request.JockeyId, request.Query, request.DisplayName, request.NormalizedName, request.AffiliationCode, request.AliasValue, request.Page, request.PageSize, request.SortBy, request.SortDescending, cancellationToken);
    public Task<IApiResponse> UpdateJockeyProfileAsync(global::HorseRacingPrediction.Contracts.Jockeys.UpdateJockeyProfileRequest request, CancellationToken cancellationToken = default) => transport.UpdateJockeyProfileAsync(request.JockeyId, request, cancellationToken);
}
