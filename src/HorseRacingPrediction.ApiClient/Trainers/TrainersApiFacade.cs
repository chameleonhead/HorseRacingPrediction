using Refit;

namespace HorseRacingPrediction.ApiClient.Trainers;

internal sealed class TrainersApiFacade(ITrainersTransport transport) : ITrainersApi
{
    public Task<IApiResponse> CorrectTrainerDataAsync(global::HorseRacingPrediction.Contracts.Trainers.CorrectTrainerDataRequest request, CancellationToken cancellationToken = default) => transport.CorrectTrainerDataAsync(request.TrainerId, request, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Trainers.GetTrainerParticipationsResponse>> GetTrainerParticipationsAsync(global::HorseRacingPrediction.Contracts.Trainers.GetTrainerParticipationsRequest request, CancellationToken cancellationToken = default) => transport.GetTrainerParticipationsAsync(request.TrainerId, request.Take, request.Skip, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Trainers.GetTrainerProfileResponse>> GetTrainerProfileAsync(global::HorseRacingPrediction.Contracts.Trainers.GetTrainerProfileRequest request, CancellationToken cancellationToken = default) => transport.GetTrainerProfileAsync(request.TrainerId, cancellationToken);
    public Task<IApiResponse> MergeTrainerAliasAsync(global::HorseRacingPrediction.Contracts.Trainers.MergeTrainerAliasRequest request, CancellationToken cancellationToken = default) => transport.MergeTrainerAliasAsync(request.TrainerId, request, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Trainers.RegisterTrainerResponse>> RegisterTrainerAsync(global::HorseRacingPrediction.Contracts.Trainers.RegisterTrainerRequest request, CancellationToken cancellationToken = default) => transport.RegisterTrainerAsync(request, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Trainers.SearchTrainersResponse>> SearchTrainersAsync(global::HorseRacingPrediction.Contracts.Trainers.SearchTrainersRequest request, CancellationToken cancellationToken = default) => transport.SearchTrainersAsync(request.TrainerId, request.Query, request.DisplayName, request.NormalizedName, request.AffiliationCode, request.AliasValue, request.Page, request.PageSize, request.SortBy, request.SortDescending, cancellationToken);
    public Task<IApiResponse> UpdateTrainerProfileAsync(global::HorseRacingPrediction.Contracts.Trainers.UpdateTrainerProfileRequest request, CancellationToken cancellationToken = default) => transport.UpdateTrainerProfileAsync(request.TrainerId, request, cancellationToken);
}
