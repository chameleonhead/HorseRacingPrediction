using Refit;

namespace HorseRacingPrediction.ApiClient.Races;

public interface IRacesApi
{
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Races.ApplyRaceEntryRepairResponse>> ApplyRaceEntryRepairAsync(global::HorseRacingPrediction.Contracts.Races.ApplyRaceEntryRepairRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> CloseRaceLifecycleAsync(global::HorseRacingPrediction.Contracts.Races.CloseRaceLifecycleRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> CorrectRaceDataAsync(global::HorseRacingPrediction.Contracts.Races.CorrectRaceDataRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Races.CreateRaceResponse>> CreateRaceAsync(global::HorseRacingPrediction.Contracts.Races.CreateRaceRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Races.CreateRaceFromScheduleResponse>> CreateRaceFromScheduleAsync(global::HorseRacingPrediction.Contracts.Races.CreateRaceFromScheduleRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> CreateRaceOddsSnapshotAsync(global::HorseRacingPrediction.Contracts.Races.CreateRaceOddsSnapshotRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> DeclareEntryResultAsync(global::HorseRacingPrediction.Contracts.Races.DeclareEntryResultRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> DeclarePayoutResultAsync(global::HorseRacingPrediction.Contracts.Races.DeclarePayoutResultRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Races.DeclareRaceResultBulkResponse>> DeclareRaceResultBulkAsync(global::HorseRacingPrediction.Contracts.Races.DeclareRaceResultBulkRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> DeclareRaceResultAsync(global::HorseRacingPrediction.Contracts.Races.DeclareRaceResultRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Races.GetPredictionComparisonResponse>> GetPredictionComparisonAsync(global::HorseRacingPrediction.Contracts.Races.GetPredictionComparisonRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Races.GetRaceAssignmentFenceResponse>> GetRaceAssignmentFenceAsync(global::HorseRacingPrediction.Contracts.Races.GetRaceAssignmentFenceRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Races.GetRaceResponse>> GetRaceAsync(global::HorseRacingPrediction.Contracts.Races.GetRaceRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Races.GetRaceEntryRepairResponse>> GetRaceEntryRepairAsync(global::HorseRacingPrediction.Contracts.Races.GetRaceEntryRepairRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Races.GetRaceEntryRepairHoldResponse>> GetRaceEntryRepairHoldAsync(global::HorseRacingPrediction.Contracts.Races.GetRaceEntryRepairHoldRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Races.GetRacePredictionContextResponse>> GetRacePredictionContextAsync(global::HorseRacingPrediction.Contracts.Races.GetRacePredictionContextRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Races.ListRaceOddsSnapshotsResponse>> ListRaceOddsSnapshotsAsync(global::HorseRacingPrediction.Contracts.Races.ListRaceOddsSnapshotsRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> MarkRaceRescheduledAsync(global::HorseRacingPrediction.Contracts.Races.MarkRaceRescheduledRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> OpenPreRaceAsync(global::HorseRacingPrediction.Contracts.Races.OpenPreRaceRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Races.PreviewRaceEntryRepairResponse>> PreviewRaceEntryRepairAsync(global::HorseRacingPrediction.Contracts.Races.PreviewRaceEntryRepairRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> PublishRaceCardAsync(global::HorseRacingPrediction.Contracts.Races.PublishRaceCardRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> RecordTrackConditionAsync(global::HorseRacingPrediction.Contracts.Races.RecordTrackConditionRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> RecordWeatherObservationAsync(global::HorseRacingPrediction.Contracts.Races.RecordWeatherObservationRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Races.RegisterEntryResponse>> RegisterEntryAsync(global::HorseRacingPrediction.Contracts.Races.RegisterEntryRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Races.ReleaseRaceEntryRepairHoldResponse>> ReleaseRaceEntryRepairHoldAsync(global::HorseRacingPrediction.Contracts.Races.ReleaseRaceEntryRepairHoldRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Races.SearchRacesResponse>> SearchRacesAsync(global::HorseRacingPrediction.Contracts.Races.SearchRacesRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> StartRaceAsync(global::HorseRacingPrediction.Contracts.Races.StartRaceRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Races.UpdateEntryCollectedDataResponse>> UpdateEntryCollectedDataAsync(global::HorseRacingPrediction.Contracts.Races.UpdateEntryCollectedDataRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Races.UpdateRaceEntryRepairHoldResponse>> UpdateRaceEntryRepairHoldAsync(global::HorseRacingPrediction.Contracts.Races.UpdateRaceEntryRepairHoldRequest request, CancellationToken cancellationToken = default);
}
