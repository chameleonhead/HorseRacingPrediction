using HorseRacingPrediction.Contracts.Jockeys;
using Refit;

namespace HorseRacingPrediction.ApiClient.Jockeys;

internal sealed class JockeysApiFacade(IJockeysTransport transport) : IJockeysApi
{
    public Task<IApiResponse> CorrectJockeyDataAsync(CorrectJockeyDataRequest request, CancellationToken cancellationToken = default) => transport.CorrectJockeyDataAsync(request.JockeyId, request, cancellationToken);
    public Task<ApiResponse<GetJockeyParticipationsResponse>> GetJockeyParticipationsAsync(GetJockeyParticipationsRequest request, CancellationToken cancellationToken = default) => transport.GetJockeyParticipationsAsync(request.JockeyId, request.Take, request.Skip, cancellationToken);
    public Task<ApiResponse<GetJockeyProfileResponse>> GetJockeyProfileAsync(GetJockeyProfileRequest request, CancellationToken cancellationToken = default) => transport.GetJockeyProfileAsync(request.JockeyId, cancellationToken);
    public Task<ApiResponse<GetJockeyRaceHistoryResponse>> GetJockeyRaceHistoryAsync(GetJockeyRaceHistoryRequest request, CancellationToken cancellationToken = default) => transport.GetJockeyRaceHistoryAsync(request.JockeyId, cancellationToken);
    public Task<IApiResponse> MergeJockeyAliasAsync(MergeJockeyAliasRequest request, CancellationToken cancellationToken = default) => transport.MergeJockeyAliasAsync(request.JockeyId, request, cancellationToken);
    public Task<ApiResponse<RegisterJockeyResponse>> RegisterJockeyAsync(RegisterJockeyRequest request, CancellationToken cancellationToken = default) => transport.RegisterJockeyAsync(request, cancellationToken);
    public Task<ApiResponse<SearchJockeysResponse>> SearchJockeysAsync(SearchJockeysRequest request, CancellationToken cancellationToken = default) => transport.SearchJockeysAsync(request.JockeyId, request.Query, request.DisplayName, request.NormalizedName, request.AffiliationCode, request.AliasValue, request.Page, request.PageSize, request.SortBy, request.SortDescending, cancellationToken);
    public Task<IApiResponse> UpdateJockeyProfileAsync(UpdateJockeyProfileRequest request, CancellationToken cancellationToken = default) => transport.UpdateJockeyProfileAsync(request.JockeyId, request, cancellationToken);
}
