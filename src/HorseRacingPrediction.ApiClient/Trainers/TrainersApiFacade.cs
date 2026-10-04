using HorseRacingPrediction.Contracts.Trainers;
using Refit;

namespace HorseRacingPrediction.ApiClient.Trainers;

internal sealed class TrainersApiFacade(ITrainersTransport transport) : ITrainersApi
{
    public Task<IApiResponse> CorrectTrainerDataAsync(CorrectTrainerDataRequest request, CancellationToken cancellationToken = default) => transport.CorrectTrainerDataAsync(request.TrainerId, request, cancellationToken);
    public Task<ApiResponse<GetTrainerParticipationsResponse>> GetTrainerParticipationsAsync(GetTrainerParticipationsRequest request, CancellationToken cancellationToken = default) => transport.GetTrainerParticipationsAsync(request.TrainerId, request.Take, request.Skip, cancellationToken);
    public Task<ApiResponse<GetTrainerProfileResponse>> GetTrainerProfileAsync(GetTrainerProfileRequest request, CancellationToken cancellationToken = default) => transport.GetTrainerProfileAsync(request.TrainerId, cancellationToken);
    public Task<IApiResponse> MergeTrainerAliasAsync(MergeTrainerAliasRequest request, CancellationToken cancellationToken = default) => transport.MergeTrainerAliasAsync(request.TrainerId, request, cancellationToken);
    public Task<ApiResponse<RegisterTrainerResponse>> RegisterTrainerAsync(RegisterTrainerRequest request, CancellationToken cancellationToken = default) => transport.RegisterTrainerAsync(request, cancellationToken);
    public Task<ApiResponse<SearchTrainersResponse>> SearchTrainersAsync(SearchTrainersRequest request, CancellationToken cancellationToken = default) => transport.SearchTrainersAsync(request.TrainerId, request.Query, request.DisplayName, request.NormalizedName, request.AffiliationCode, request.AliasValue, request.Page, request.PageSize, request.SortBy, request.SortDescending, cancellationToken);
    public Task<IApiResponse> UpdateTrainerProfileAsync(UpdateTrainerProfileRequest request, CancellationToken cancellationToken = default) => transport.UpdateTrainerProfileAsync(request.TrainerId, request, cancellationToken);
}
