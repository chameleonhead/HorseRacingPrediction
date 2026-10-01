using Refit;
using HorseRacingPrediction.Contracts.Jockeys;

namespace HorseRacingPrediction.ApiClient.Jockeys;

internal interface IJockeysTransport
{
    [Patch("/api/jockeys/{jockeyId}")] Task<IApiResponse> CorrectJockeyDataAsync([AliasAs("jockeyId")] string jockeyId, [Body] global::HorseRacingPrediction.Contracts.Jockeys.CorrectJockeyDataRequest request, CancellationToken cancellationToken);
    [Get("/api/jockeys/{jockeyId}/participations")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Jockeys.GetJockeyParticipationsResponse>> GetJockeyParticipationsAsync([AliasAs("jockeyId")] string jockeyId, [AliasAs("take")] int? take, [AliasAs("skip")] int? skip, CancellationToken cancellationToken);
    [Get("/api/jockeys/{jockeyId}")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Jockeys.GetJockeyProfileResponse>> GetJockeyProfileAsync([AliasAs("jockeyId")] string jockeyId, CancellationToken cancellationToken);
    [Get("/api/jockeys/{jockeyId}/race-history")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Jockeys.GetJockeyRaceHistoryResponse>> GetJockeyRaceHistoryAsync([AliasAs("jockeyId")] string jockeyId, CancellationToken cancellationToken);
    [Post("/api/jockeys/{jockeyId}/aliases")] Task<IApiResponse> MergeJockeyAliasAsync([AliasAs("jockeyId")] string jockeyId, [Body] global::HorseRacingPrediction.Contracts.Jockeys.MergeJockeyAliasRequest request, CancellationToken cancellationToken);
    [Post("/api/jockeys")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Jockeys.RegisterJockeyResponse>> RegisterJockeyAsync([Body] global::HorseRacingPrediction.Contracts.Jockeys.RegisterJockeyRequest request, CancellationToken cancellationToken);
    [Get("/api/jockeys")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Jockeys.SearchJockeysResponse>> SearchJockeysAsync([AliasAs("jockeyId")] string? jockeyId, [AliasAs("query")] string? query, [AliasAs("displayName")] string? displayName, [AliasAs("normalizedName")] string? normalizedName, [AliasAs("affiliationCode")] string? affiliationCode, [AliasAs("aliasValue")] string? aliasValue, [AliasAs("page")] int? page, [AliasAs("pageSize")] int? pageSize, [AliasAs("sortBy")] string? sortBy, [AliasAs("sortDescending")] bool? sortDescending, CancellationToken cancellationToken);
    [Put("/api/jockeys/{jockeyId}")] Task<IApiResponse> UpdateJockeyProfileAsync([AliasAs("jockeyId")] string jockeyId, [Body] global::HorseRacingPrediction.Contracts.Jockeys.UpdateJockeyProfileRequest request, CancellationToken cancellationToken);
}
