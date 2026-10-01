using Refit;

namespace HorseRacingPrediction.ApiClient.Trainers;

public interface ITrainersApi
{
    Task<IApiResponse> CorrectTrainerDataAsync(global::HorseRacingPrediction.Contracts.Trainers.CorrectTrainerDataRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Trainers.GetTrainerParticipationsResponse>> GetTrainerParticipationsAsync(global::HorseRacingPrediction.Contracts.Trainers.GetTrainerParticipationsRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Trainers.GetTrainerProfileResponse>> GetTrainerProfileAsync(global::HorseRacingPrediction.Contracts.Trainers.GetTrainerProfileRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> MergeTrainerAliasAsync(global::HorseRacingPrediction.Contracts.Trainers.MergeTrainerAliasRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Trainers.RegisterTrainerResponse>> RegisterTrainerAsync(global::HorseRacingPrediction.Contracts.Trainers.RegisterTrainerRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Trainers.SearchTrainersResponse>> SearchTrainersAsync(global::HorseRacingPrediction.Contracts.Trainers.SearchTrainersRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> UpdateTrainerProfileAsync(global::HorseRacingPrediction.Contracts.Trainers.UpdateTrainerProfileRequest request, CancellationToken cancellationToken = default);
}
