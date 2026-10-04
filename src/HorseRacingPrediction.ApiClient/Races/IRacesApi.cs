using HorseRacingPrediction.Contracts.Races;
using Refit;

namespace HorseRacingPrediction.ApiClient.Races;

public interface IRacesApi
{
    Task<ApiResponse<ApplyRaceEntryRepairResponse>> ApplyRaceEntryRepairAsync(ApplyRaceEntryRepairRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> CloseRaceLifecycleAsync(CloseRaceLifecycleRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> CorrectRaceDataAsync(CorrectRaceDataRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<CreateRaceResponse>> CreateRaceAsync(CreateRaceRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<CreateRaceFromScheduleResponse>> CreateRaceFromScheduleAsync(CreateRaceFromScheduleRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> CreateRaceOddsSnapshotAsync(CreateRaceOddsSnapshotRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> DeclareEntryResultAsync(DeclareEntryResultRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> DeclarePayoutResultAsync(DeclarePayoutResultRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<DeclareRaceResultBulkResponse>> DeclareRaceResultBulkAsync(DeclareRaceResultBulkRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> DeclareRaceResultAsync(DeclareRaceResultRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetPredictionComparisonResponse>> GetPredictionComparisonAsync(GetPredictionComparisonRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetRaceAssignmentFenceResponse>> GetRaceAssignmentFenceAsync(GetRaceAssignmentFenceRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetRaceResponse>> GetRaceAsync(GetRaceRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetRaceEntryRepairResponse>> GetRaceEntryRepairAsync(GetRaceEntryRepairRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetRaceEntryRepairHoldResponse>> GetRaceEntryRepairHoldAsync(GetRaceEntryRepairHoldRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetRacePredictionContextResponse>> GetRacePredictionContextAsync(GetRacePredictionContextRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ListRaceOddsSnapshotsResponse>> ListRaceOddsSnapshotsAsync(ListRaceOddsSnapshotsRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> MarkRaceRescheduledAsync(MarkRaceRescheduledRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> OpenPreRaceAsync(OpenPreRaceRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PreviewRaceEntryRepairResponse>> PreviewRaceEntryRepairAsync(PreviewRaceEntryRepairRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> PublishRaceCardAsync(PublishRaceCardRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> RecordTrackConditionAsync(RecordTrackConditionRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> RecordWeatherObservationAsync(RecordWeatherObservationRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<RegisterEntryResponse>> RegisterEntryAsync(RegisterEntryRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ReleaseRaceEntryRepairHoldResponse>> ReleaseRaceEntryRepairHoldAsync(ReleaseRaceEntryRepairHoldRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<SearchRacesResponse>> SearchRacesAsync(SearchRacesRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> StartRaceAsync(StartRaceRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<UpdateEntryCollectedDataResponse>> UpdateEntryCollectedDataAsync(UpdateEntryCollectedDataRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<UpdateRaceEntryRepairHoldResponse>> UpdateRaceEntryRepairHoldAsync(UpdateRaceEntryRepairHoldRequest request, CancellationToken cancellationToken = default);
}
