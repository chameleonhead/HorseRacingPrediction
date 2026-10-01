using Refit;
using HorseRacingPrediction.Contracts.Trainers;

namespace HorseRacingPrediction.ApiClient.Trainers;

internal interface ITrainersTransport
{
    [Patch("/api/trainers/{trainerId}")] Task<IApiResponse> CorrectTrainerDataAsync([AliasAs("trainerId")] string trainerId, [Body] global::HorseRacingPrediction.Contracts.Trainers.CorrectTrainerDataRequest request, CancellationToken cancellationToken);
    [Get("/api/trainers/{trainerId}/participations")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Trainers.GetTrainerParticipationsResponse>> GetTrainerParticipationsAsync([AliasAs("trainerId")] string trainerId, [AliasAs("take")] int? take, [AliasAs("skip")] int? skip, CancellationToken cancellationToken);
    [Get("/api/trainers/{trainerId}")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Trainers.GetTrainerProfileResponse>> GetTrainerProfileAsync([AliasAs("trainerId")] string trainerId, CancellationToken cancellationToken);
    [Post("/api/trainers/{trainerId}/aliases")] Task<IApiResponse> MergeTrainerAliasAsync([AliasAs("trainerId")] string trainerId, [Body] global::HorseRacingPrediction.Contracts.Trainers.MergeTrainerAliasRequest request, CancellationToken cancellationToken);
    [Post("/api/trainers")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Trainers.RegisterTrainerResponse>> RegisterTrainerAsync([Body] global::HorseRacingPrediction.Contracts.Trainers.RegisterTrainerRequest request, CancellationToken cancellationToken);
    [Get("/api/trainers")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Trainers.SearchTrainersResponse>> SearchTrainersAsync([AliasAs("trainerId")] string? trainerId, [AliasAs("query")] string? query, [AliasAs("displayName")] string? displayName, [AliasAs("normalizedName")] string? normalizedName, [AliasAs("affiliationCode")] string? affiliationCode, [AliasAs("aliasValue")] string? aliasValue, [AliasAs("page")] int? page, [AliasAs("pageSize")] int? pageSize, [AliasAs("sortBy")] string? sortBy, [AliasAs("sortDescending")] bool? sortDescending, CancellationToken cancellationToken);
    [Put("/api/trainers/{trainerId}")] Task<IApiResponse> UpdateTrainerProfileAsync([AliasAs("trainerId")] string trainerId, [Body] global::HorseRacingPrediction.Contracts.Trainers.UpdateTrainerProfileRequest request, CancellationToken cancellationToken);
}
