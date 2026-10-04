using HorseRacingPrediction.Contracts.Trainers;
using Refit;

namespace HorseRacingPrediction.ApiClient.Trainers;

public interface ITrainersApi
{
    Task<IApiResponse> CorrectTrainerDataAsync(CorrectTrainerDataRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetTrainerParticipationsResponse>> GetTrainerParticipationsAsync(GetTrainerParticipationsRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetTrainerProfileResponse>> GetTrainerProfileAsync(GetTrainerProfileRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> MergeTrainerAliasAsync(MergeTrainerAliasRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<RegisterTrainerResponse>> RegisterTrainerAsync(RegisterTrainerRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<SearchTrainersResponse>> SearchTrainersAsync(SearchTrainersRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> UpdateTrainerProfileAsync(UpdateTrainerProfileRequest request, CancellationToken cancellationToken = default);
}
