using HorseRacingPrediction.Contracts.Horses;
using Refit;

namespace HorseRacingPrediction.ApiClient.Horses;

internal interface IHorsesTransport
{
    [Patch("/api/horses/{horseId}")] Task<IApiResponse> CorrectHorseDataAsync([AliasAs("horseId")] string horseId, [Body] CorrectHorseDataRequest request, CancellationToken cancellationToken);
    [Get("/api/horses/{horseId}/participations")] Task<ApiResponse<GetHorseParticipationsResponse>> GetHorseParticipationsAsync([AliasAs("horseId")] string horseId, [AliasAs("take")] int? take, [AliasAs("skip")] int? skip, CancellationToken cancellationToken);
    [Get("/api/horses/{horseId}")] Task<ApiResponse<GetHorseProfileResponse>> GetHorseProfileAsync([AliasAs("horseId")] string horseId, CancellationToken cancellationToken);
    [Get("/api/horses/{horseId}/race-history")] Task<ApiResponse<GetHorseRaceHistoryResponse>> GetHorseRaceHistoryAsync([AliasAs("horseId")] string horseId, CancellationToken cancellationToken);
    [Get("/api/horses/{horseId}/weight-history")] Task<ApiResponse<GetHorseWeightHistoryResponse>> GetHorseWeightHistoryAsync([AliasAs("horseId")] string horseId, CancellationToken cancellationToken);
    [Post("/api/horses/{horseId}/aliases")] Task<IApiResponse> MergeHorseAliasAsync([AliasAs("horseId")] string horseId, [Body] MergeHorseAliasRequest request, CancellationToken cancellationToken);
    [Post("/api/horses")] Task<ApiResponse<RegisterHorseResponse>> RegisterHorseAsync([Body] RegisterHorseRequest request, CancellationToken cancellationToken);
    [Get("/api/horses")] Task<ApiResponse<SearchHorsesResponse>> SearchHorsesAsync([AliasAs("horseId")] string? horseId, [AliasAs("query")] string? query, [AliasAs("registeredName")] string? registeredName, [AliasAs("normalizedName")] string? normalizedName, [AliasAs("sexCode")] string? sexCode, [AliasAs("birthDateFrom")] DateOnly? birthDateFrom, [AliasAs("birthDateTo")] DateOnly? birthDateTo, [AliasAs("aliasValue")] string? aliasValue, [AliasAs("page")] int? page, [AliasAs("pageSize")] int? pageSize, [AliasAs("sortBy")] string? sortBy, [AliasAs("sortDescending")] bool? sortDescending, CancellationToken cancellationToken);
    [Put("/api/horses/{horseId}")] Task<IApiResponse> UpdateHorseProfileAsync([AliasAs("horseId")] string horseId, [Body] UpdateHorseProfileRequest request, CancellationToken cancellationToken);
}
