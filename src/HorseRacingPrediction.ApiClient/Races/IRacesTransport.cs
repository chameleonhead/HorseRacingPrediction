using HorseRacingPrediction.Contracts.Races;
using Refit;

namespace HorseRacingPrediction.ApiClient.Races;

internal interface IRacesTransport
{
    [Post("/api/v2/admin/races/{raceId}/entry-repairs")] Task<ApiResponse<ApplyRaceEntryRepairResponse>> ApplyRaceEntryRepairAsync([AliasAs("raceId")] string raceId, [Body] ApplyRaceEntryRepairRequest request, CancellationToken cancellationToken);
    [Post("/api/races/{raceId}/close")] Task<IApiResponse> CloseRaceLifecycleAsync([AliasAs("raceId")] string raceId, CancellationToken cancellationToken);
    [Patch("/api/races/{raceId}")] Task<IApiResponse> CorrectRaceDataAsync([AliasAs("raceId")] string raceId, [Body] CorrectRaceDataRequest request, CancellationToken cancellationToken);
    [Post("/api/races")] Task<ApiResponse<CreateRaceResponse>> CreateRaceAsync([Body] CreateRaceRequest request, CancellationToken cancellationToken);
    [Post("/api/v2/admin/races")] Task<ApiResponse<CreateRaceFromScheduleResponse>> CreateRaceFromScheduleAsync([Body] CreateRaceFromScheduleRequest request, CancellationToken cancellationToken);
    [Post("/api/v2/admin/races/{raceId}/odds-snapshot-records")] Task<IApiResponse> CreateRaceOddsSnapshotAsync([AliasAs("raceId")] string raceId, [Body] CreateRaceOddsSnapshotRequest request, CancellationToken cancellationToken);
    [Post("/api/races/{raceId}/entries/{entryId}/result")] Task<IApiResponse> DeclareEntryResultAsync([AliasAs("raceId")] string raceId, [AliasAs("entryId")] string entryId, [Body] DeclareEntryResultRequest request, CancellationToken cancellationToken);
    [Post("/api/races/{raceId}/payout")] Task<IApiResponse> DeclarePayoutResultAsync([AliasAs("raceId")] string raceId, [Body] DeclarePayoutResultRequest request, CancellationToken cancellationToken);
    [Post("/api/races/result-bulk")] Task<ApiResponse<DeclareRaceResultBulkResponse>> DeclareRaceResultBulkAsync([Body] DeclareRaceResultBulkRequest request, CancellationToken cancellationToken);
    [Post("/api/races/{raceId}/result")] Task<IApiResponse> DeclareRaceResultAsync([AliasAs("raceId")] string raceId, [Body] DeclareRaceResultRequest request, CancellationToken cancellationToken);
    [Get("/api/races/{raceId}/comparison")] Task<ApiResponse<GetPredictionComparisonResponse>> GetPredictionComparisonAsync([AliasAs("raceId")] string raceId, CancellationToken cancellationToken);
    [Get("/api/v2/admin/races/{raceId}/entry-repair/assignment-fence-state")] Task<ApiResponse<GetRaceAssignmentFenceResponse>> GetRaceAssignmentFenceAsync([AliasAs("raceId")] string raceId, CancellationToken cancellationToken);
    [Get("/api/races/{raceId}")] Task<ApiResponse<GetRaceResponse>> GetRaceAsync([AliasAs("raceId")] string raceId, CancellationToken cancellationToken);
    [Get("/api/v2/admin/races/{raceId}/entry-repair/inspection")] Task<ApiResponse<GetRaceEntryRepairResponse>> GetRaceEntryRepairAsync([AliasAs("raceId")] string raceId, CancellationToken cancellationToken);
    [Get("/api/v2/admin/races/{raceId}/entry-repair/hold-state")] Task<ApiResponse<GetRaceEntryRepairHoldResponse>> GetRaceEntryRepairHoldAsync([AliasAs("raceId")] string raceId, CancellationToken cancellationToken);
    [Get("/api/races/{raceId}/context")] Task<ApiResponse<GetRacePredictionContextResponse>> GetRacePredictionContextAsync([AliasAs("raceId")] string raceId, CancellationToken cancellationToken);
    [Get("/api/v2/admin/races/{raceId}/odds-snapshot-records")] Task<ApiResponse<ListRaceOddsSnapshotsResponse>> ListRaceOddsSnapshotsAsync([AliasAs("raceId")] string raceId, CancellationToken cancellationToken);
    [Post("/api/races/{raceId}/reschedule")] Task<IApiResponse> MarkRaceRescheduledAsync([AliasAs("raceId")] string raceId, [Body] MarkRaceRescheduledRequest request, CancellationToken cancellationToken);
    [Post("/api/races/{raceId}/open-pre-race")] Task<IApiResponse> OpenPreRaceAsync([AliasAs("raceId")] string raceId, CancellationToken cancellationToken);
    [Post("/api/v2/admin/races/{raceId}/entry-repair-previews")] Task<ApiResponse<PreviewRaceEntryRepairResponse>> PreviewRaceEntryRepairAsync([AliasAs("raceId")] string raceId, [Body] PreviewRaceEntryRepairRequest request, CancellationToken cancellationToken);
    [Post("/api/races/{raceId}/card/publish")] Task<IApiResponse> PublishRaceCardAsync([AliasAs("raceId")] string raceId, [Body] PublishRaceCardRequest request, CancellationToken cancellationToken);
    [Post("/api/races/{raceId}/track-condition")] Task<IApiResponse> RecordTrackConditionAsync([AliasAs("raceId")] string raceId, [Body] RecordTrackConditionRequest request, CancellationToken cancellationToken);
    [Post("/api/races/{raceId}/weather")] Task<IApiResponse> RecordWeatherObservationAsync([AliasAs("raceId")] string raceId, [Body] RecordWeatherObservationRequest request, CancellationToken cancellationToken);
    [Post("/api/races/{raceId}/entries")] Task<ApiResponse<RegisterEntryResponse>> RegisterEntryAsync([AliasAs("raceId")] string raceId, [Body] RegisterEntryRequest request, CancellationToken cancellationToken);
    [Patch("/api/v2/admin/races/{raceId}/entry-repair/hold")] Task<ApiResponse<ReleaseRaceEntryRepairHoldResponse>> ReleaseRaceEntryRepairHoldAsync([AliasAs("raceId")] string raceId, [Body] ReleaseRaceEntryRepairHoldRequest request, CancellationToken cancellationToken);
    [Get("/api/races")] Task<ApiResponse<SearchRacesResponse>> SearchRacesAsync([AliasAs("raceId")] string? raceId, [AliasAs("raceDateFrom")] DateOnly? raceDateFrom, [AliasAs("raceDateTo")] DateOnly? raceDateTo, [AliasAs("racecourseCode")] string? racecourseCode, [AliasAs("raceNumber")] int? raceNumber, [AliasAs("raceName")] string? raceName, [AliasAs("status")] RaceStatus? status, [AliasAs("winningHorseName")] string? winningHorseName, [AliasAs("page")] int? page, [AliasAs("pageSize")] int? pageSize, [AliasAs("sortBy")] string? sortBy, [AliasAs("sortDescending")] bool? sortDescending, CancellationToken cancellationToken);
    [Post("/api/races/{raceId}/start")] Task<IApiResponse> StartRaceAsync([AliasAs("raceId")] string raceId, CancellationToken cancellationToken);
    [Put("/api/races/{raceId}/entries/{entryId}")] Task<ApiResponse<UpdateEntryCollectedDataResponse>> UpdateEntryCollectedDataAsync([AliasAs("raceId")] string raceId, [AliasAs("entryId")] string entryId, [Body] UpdateEntryCollectedDataRequest request, CancellationToken cancellationToken);
    [Put("/api/v2/admin/races/{raceId}/entry-repair/hold")] Task<ApiResponse<UpdateRaceEntryRepairHoldResponse>> UpdateRaceEntryRepairHoldAsync([AliasAs("raceId")] string raceId, [Body] UpdateRaceEntryRepairHoldRequest request, CancellationToken cancellationToken);
}
