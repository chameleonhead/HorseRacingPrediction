using Refit;

namespace HorseRacingPrediction.ApiClient.Horses;

public interface IHorsesApi
{
    Task<IApiResponse> CorrectHorseDataAsync(global::HorseRacingPrediction.Contracts.Horses.CorrectHorseDataRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Horses.GetHorseParticipationsResponse>> GetHorseParticipationsAsync(global::HorseRacingPrediction.Contracts.Horses.GetHorseParticipationsRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Horses.GetHorseProfileResponse>> GetHorseProfileAsync(global::HorseRacingPrediction.Contracts.Horses.GetHorseProfileRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Horses.GetHorseRaceHistoryResponse>> GetHorseRaceHistoryAsync(global::HorseRacingPrediction.Contracts.Horses.GetHorseRaceHistoryRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Horses.GetHorseWeightHistoryResponse>> GetHorseWeightHistoryAsync(global::HorseRacingPrediction.Contracts.Horses.GetHorseWeightHistoryRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> MergeHorseAliasAsync(global::HorseRacingPrediction.Contracts.Horses.MergeHorseAliasRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Horses.RegisterHorseResponse>> RegisterHorseAsync(global::HorseRacingPrediction.Contracts.Horses.RegisterHorseRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Horses.SearchHorsesResponse>> SearchHorsesAsync(global::HorseRacingPrediction.Contracts.Horses.SearchHorsesRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> UpdateHorseProfileAsync(global::HorseRacingPrediction.Contracts.Horses.UpdateHorseProfileRequest request, CancellationToken cancellationToken = default);
}
