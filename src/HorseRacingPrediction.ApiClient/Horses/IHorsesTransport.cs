using Refit;
using HorseRacingPrediction.Contracts.Horses;

namespace HorseRacingPrediction.ApiClient.Horses;

internal interface IHorsesTransport
{
    [Patch("/api/horses/{horseId}")] Task<IApiResponse> CorrectHorseDataAsync([AliasAs("horseId")] string horseId, [Body] global::HorseRacingPrediction.Contracts.Horses.CorrectHorseDataRequest request, CancellationToken cancellationToken);
    [Get("/api/horses/{horseId}/participations")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Horses.GetHorseParticipationsResponse>> GetHorseParticipationsAsync([AliasAs("horseId")] string horseId, [AliasAs("take")] int? take, [AliasAs("skip")] int? skip, CancellationToken cancellationToken);
    [Get("/api/horses/{horseId}")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Horses.GetHorseProfileResponse>> GetHorseProfileAsync([AliasAs("horseId")] string horseId, CancellationToken cancellationToken);
    [Get("/api/horses/{horseId}/race-history")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Horses.GetHorseRaceHistoryResponse>> GetHorseRaceHistoryAsync([AliasAs("horseId")] string horseId, CancellationToken cancellationToken);
    [Get("/api/horses/{horseId}/weight-history")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Horses.GetHorseWeightHistoryResponse>> GetHorseWeightHistoryAsync([AliasAs("horseId")] string horseId, CancellationToken cancellationToken);
    [Post("/api/horses/{horseId}/aliases")] Task<IApiResponse> MergeHorseAliasAsync([AliasAs("horseId")] string horseId, [Body] global::HorseRacingPrediction.Contracts.Horses.MergeHorseAliasRequest request, CancellationToken cancellationToken);
    [Post("/api/horses")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Horses.RegisterHorseResponse>> RegisterHorseAsync([Body] global::HorseRacingPrediction.Contracts.Horses.RegisterHorseRequest request, CancellationToken cancellationToken);
    [Get("/api/horses")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Horses.SearchHorsesResponse>> SearchHorsesAsync([AliasAs("horseId")] string? horseId, [AliasAs("query")] string? query, [AliasAs("registeredName")] string? registeredName, [AliasAs("normalizedName")] string? normalizedName, [AliasAs("sexCode")] string? sexCode, [AliasAs("birthDateFrom")] DateOnly? birthDateFrom, [AliasAs("birthDateTo")] DateOnly? birthDateTo, [AliasAs("aliasValue")] string? aliasValue, [AliasAs("page")] int? page, [AliasAs("pageSize")] int? pageSize, [AliasAs("sortBy")] string? sortBy, [AliasAs("sortDescending")] bool? sortDescending, CancellationToken cancellationToken);
    [Put("/api/horses/{horseId}")] Task<IApiResponse> UpdateHorseProfileAsync([AliasAs("horseId")] string horseId, [Body] global::HorseRacingPrediction.Contracts.Horses.UpdateHorseProfileRequest request, CancellationToken cancellationToken);
}
