using HorseRacingPrediction.Contracts.Trainers;
using Refit;

namespace HorseRacingPrediction.ApiClient.Trainers;

internal interface ITrainersTransport
{
    [Patch("/api/trainers/{trainerId}")] Task<IApiResponse> CorrectTrainerDataAsync([AliasAs("trainerId")] string trainerId, [Body] CorrectTrainerDataRequest request, CancellationToken cancellationToken);
    [Get("/api/trainers/{trainerId}/participations")] Task<ApiResponse<GetTrainerParticipationsResponse>> GetTrainerParticipationsAsync([AliasAs("trainerId")] string trainerId, [AliasAs("take")] int? take, [AliasAs("skip")] int? skip, CancellationToken cancellationToken);
    [Get("/api/trainers/{trainerId}")] Task<ApiResponse<GetTrainerProfileResponse>> GetTrainerProfileAsync([AliasAs("trainerId")] string trainerId, CancellationToken cancellationToken);
    [Post("/api/trainers/{trainerId}/aliases")] Task<IApiResponse> MergeTrainerAliasAsync([AliasAs("trainerId")] string trainerId, [Body] MergeTrainerAliasRequest request, CancellationToken cancellationToken);
    [Post("/api/trainers")] Task<ApiResponse<RegisterTrainerResponse>> RegisterTrainerAsync([Body] RegisterTrainerRequest request, CancellationToken cancellationToken);
    [Get("/api/trainers")] Task<ApiResponse<SearchTrainersResponse>> SearchTrainersAsync([AliasAs("trainerId")] string? trainerId, [AliasAs("query")] string? query, [AliasAs("displayName")] string? displayName, [AliasAs("normalizedName")] string? normalizedName, [AliasAs("affiliationCode")] string? affiliationCode, [AliasAs("aliasValue")] string? aliasValue, [AliasAs("page")] int? page, [AliasAs("pageSize")] int? pageSize, [AliasAs("sortBy")] string? sortBy, [AliasAs("sortDescending")] bool? sortDescending, CancellationToken cancellationToken);
    [Put("/api/trainers/{trainerId}")] Task<IApiResponse> UpdateTrainerProfileAsync([AliasAs("trainerId")] string trainerId, [Body] UpdateTrainerProfileRequest request, CancellationToken cancellationToken);
}
