using HorseRacingPrediction.Contracts.Races;
using Refit;

namespace HorseRacingPrediction.ApiClient.Races;

internal sealed class RacesApiFacade(IRacesTransport transport) : IRacesApi
{
    public Task<ApiResponse<ApplyRaceEntryRepairResponse>> ApplyRaceEntryRepairAsync(ApplyRaceEntryRepairRequest request, CancellationToken cancellationToken = default) => transport.ApplyRaceEntryRepairAsync(request.RaceId, request, cancellationToken);
    public Task<IApiResponse> CloseRaceLifecycleAsync(CloseRaceLifecycleRequest request, CancellationToken cancellationToken = default) => transport.CloseRaceLifecycleAsync(request.RaceId, cancellationToken);
    public Task<IApiResponse> CorrectRaceDataAsync(CorrectRaceDataRequest request, CancellationToken cancellationToken = default) => transport.CorrectRaceDataAsync(request.RaceId, request, cancellationToken);
    public Task<ApiResponse<CreateRaceResponse>> CreateRaceAsync(CreateRaceRequest request, CancellationToken cancellationToken = default) => transport.CreateRaceAsync(request, cancellationToken);
    public Task<ApiResponse<CreateRaceFromScheduleResponse>> CreateRaceFromScheduleAsync(CreateRaceFromScheduleRequest request, CancellationToken cancellationToken = default) => transport.CreateRaceFromScheduleAsync(request, cancellationToken);
    public Task<IApiResponse> CreateRaceOddsSnapshotAsync(CreateRaceOddsSnapshotRequest request, CancellationToken cancellationToken = default) => transport.CreateRaceOddsSnapshotAsync(request.RaceId, request, cancellationToken);
    public Task<IApiResponse> DeclareEntryResultAsync(DeclareEntryResultRequest request, CancellationToken cancellationToken = default) => transport.DeclareEntryResultAsync(request.RaceId, request.EntryId, request, cancellationToken);
    public Task<IApiResponse> DeclarePayoutResultAsync(DeclarePayoutResultRequest request, CancellationToken cancellationToken = default) => transport.DeclarePayoutResultAsync(request.RaceId, request, cancellationToken);
    public Task<ApiResponse<DeclareRaceResultBulkResponse>> DeclareRaceResultBulkAsync(DeclareRaceResultBulkRequest request, CancellationToken cancellationToken = default) => transport.DeclareRaceResultBulkAsync(request, cancellationToken);
    public Task<IApiResponse> DeclareRaceResultAsync(DeclareRaceResultRequest request, CancellationToken cancellationToken = default) => transport.DeclareRaceResultAsync(request.RaceId, request, cancellationToken);
    public Task<ApiResponse<GetPredictionComparisonResponse>> GetPredictionComparisonAsync(GetPredictionComparisonRequest request, CancellationToken cancellationToken = default) => transport.GetPredictionComparisonAsync(request.RaceId, cancellationToken);
    public Task<ApiResponse<GetRaceAssignmentFenceResponse>> GetRaceAssignmentFenceAsync(GetRaceAssignmentFenceRequest request, CancellationToken cancellationToken = default) => transport.GetRaceAssignmentFenceAsync(request.RaceId, cancellationToken);
    public Task<ApiResponse<GetRaceResponse>> GetRaceAsync(GetRaceRequest request, CancellationToken cancellationToken = default) => transport.GetRaceAsync(request.RaceId, cancellationToken);
    public Task<ApiResponse<GetRaceEntryRepairResponse>> GetRaceEntryRepairAsync(GetRaceEntryRepairRequest request, CancellationToken cancellationToken = default) => transport.GetRaceEntryRepairAsync(request.RaceId, cancellationToken);
    public Task<ApiResponse<GetRaceEntryRepairHoldResponse>> GetRaceEntryRepairHoldAsync(GetRaceEntryRepairHoldRequest request, CancellationToken cancellationToken = default) => transport.GetRaceEntryRepairHoldAsync(request.RaceId, cancellationToken);
    public Task<ApiResponse<GetRacePredictionContextResponse>> GetRacePredictionContextAsync(GetRacePredictionContextRequest request, CancellationToken cancellationToken = default) => transport.GetRacePredictionContextAsync(request.RaceId, cancellationToken);
    public Task<ApiResponse<ListRaceOddsSnapshotsResponse>> ListRaceOddsSnapshotsAsync(ListRaceOddsSnapshotsRequest request, CancellationToken cancellationToken = default) => transport.ListRaceOddsSnapshotsAsync(request.RaceId, cancellationToken);
    public Task<IApiResponse> MarkRaceRescheduledAsync(MarkRaceRescheduledRequest request, CancellationToken cancellationToken = default) => transport.MarkRaceRescheduledAsync(request.RaceId, request, cancellationToken);
    public Task<IApiResponse> OpenPreRaceAsync(OpenPreRaceRequest request, CancellationToken cancellationToken = default) => transport.OpenPreRaceAsync(request.RaceId, cancellationToken);
    public Task<ApiResponse<PreviewRaceEntryRepairResponse>> PreviewRaceEntryRepairAsync(PreviewRaceEntryRepairRequest request, CancellationToken cancellationToken = default) => transport.PreviewRaceEntryRepairAsync(request.RaceId, request, cancellationToken);
    public Task<IApiResponse> PublishRaceCardAsync(PublishRaceCardRequest request, CancellationToken cancellationToken = default) => transport.PublishRaceCardAsync(request.RaceId, request, cancellationToken);
    public Task<IApiResponse> RecordTrackConditionAsync(RecordTrackConditionRequest request, CancellationToken cancellationToken = default) => transport.RecordTrackConditionAsync(request.RaceId, request, cancellationToken);
    public Task<IApiResponse> RecordWeatherObservationAsync(RecordWeatherObservationRequest request, CancellationToken cancellationToken = default) => transport.RecordWeatherObservationAsync(request.RaceId, request, cancellationToken);
    public Task<ApiResponse<RegisterEntryResponse>> RegisterEntryAsync(RegisterEntryRequest request, CancellationToken cancellationToken = default) => transport.RegisterEntryAsync(request.RaceId, request, cancellationToken);
    public Task<ApiResponse<ReleaseRaceEntryRepairHoldResponse>> ReleaseRaceEntryRepairHoldAsync(ReleaseRaceEntryRepairHoldRequest request, CancellationToken cancellationToken = default) => transport.ReleaseRaceEntryRepairHoldAsync(request.RaceId, request, cancellationToken);
    public Task<ApiResponse<SearchRacesResponse>> SearchRacesAsync(SearchRacesRequest request, CancellationToken cancellationToken = default) => transport.SearchRacesAsync(request.RaceId, request.RaceDateFrom, request.RaceDateTo, request.RacecourseCode, request.RaceNumber, request.RaceName, request.Status, request.WinningHorseName, request.Page, request.PageSize, request.SortBy, request.SortDescending, cancellationToken);
    public Task<IApiResponse> StartRaceAsync(StartRaceRequest request, CancellationToken cancellationToken = default) => transport.StartRaceAsync(request.RaceId, cancellationToken);
    public Task<ApiResponse<UpdateEntryCollectedDataResponse>> UpdateEntryCollectedDataAsync(UpdateEntryCollectedDataRequest request, CancellationToken cancellationToken = default) => transport.UpdateEntryCollectedDataAsync(request.RaceId, request.EntryId, request, cancellationToken);
    public Task<ApiResponse<UpdateRaceEntryRepairHoldResponse>> UpdateRaceEntryRepairHoldAsync(UpdateRaceEntryRepairHoldRequest request, CancellationToken cancellationToken = default) => transport.UpdateRaceEntryRepairHoldAsync(request.RaceId, request, cancellationToken);
}
