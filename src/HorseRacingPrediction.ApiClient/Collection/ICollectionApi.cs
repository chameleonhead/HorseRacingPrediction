using HorseRacingPrediction.Contracts.Collection;
using Refit;

namespace HorseRacingPrediction.ApiClient.Collection;

public interface ICollectionApi
{
    Task<ApiResponse<AcquireCollectionTaskResponse>> AcquireCollectionTaskAsync(AcquireCollectionTaskRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AcquireNextExecutionResponse>> AcquireNextExecutionAsync(AcquireNextExecutionRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ApplyRaceDetailMigrationResponse>> ApplyRaceDetailMigrationAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<ApplyRaceEntryOwnerMigrationResponse>> ApplyRaceEntryOwnerMigrationAsync(CancellationToken cancellationToken = default);
    Task<IApiResponse> CancelCollectionTaskAsync(CancelCollectionTaskRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> CompleteCollectionTaskAttemptAsync(CompleteCollectionTaskAttemptRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<CreateBackfillBatchResponse>> CreateBackfillBatchAsync(CreateBackfillBatchRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<CreateCollectionRecoveryBatchResponse>> CreateCollectionRecoveryBatchAsync(CreateCollectionRecoveryBatchRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<CreateCollectionRevisionResponse>> CreateCollectionRevisionAsync(CreateCollectionRevisionRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<CreateCollectionTaskBatchResponse>> CreateCollectionTaskBatchAsync(CreateCollectionTaskBatchRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<CreateCollectionTaskResponse>> CreateCollectionTaskAsync(CreateCollectionTaskRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<CreateKnownRecoveryBatchResponse>> CreateKnownRecoveryBatchAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<CreateRaceEntryOwnerRepairBatchResponse>> CreateRaceEntryOwnerRepairBatchAsync(CreateRaceEntryOwnerRepairBatchRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<CreateRecollectionBatchResponse>> CreateRecollectionBatchAsync(CreateRecollectionBatchRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetBackfillBatchResponse>> GetBackfillBatchAsync(GetBackfillBatchRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetCollectionDashboardResponse>> GetCollectionDashboardAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<GetCollectionMonitoringFindingsResponse>> GetCollectionMonitoringFindingsAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<GetCollectionPipelineResponse>> GetCollectionPipelineAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<GetCollectionProgressResponse>> GetCollectionProgressAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<GetCollectionResourceDetailResponse>> GetCollectionResourceDetailAsync(GetCollectionResourceDetailRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetCollectionResourceStateResponse>> GetCollectionResourceStateAsync(GetCollectionResourceStateRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetExecutionBatchResponse>> GetExecutionBatchAsync(GetExecutionBatchRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetFailureNotificationGroupResponse>> GetFailureNotificationGroupAsync(GetFailureNotificationGroupRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetKnownRecoveryPreviewResponse>> GetKnownRecoveryPreviewAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<GetOwnerIdentityMigrationPreviewResponse>> GetOwnerIdentityMigrationPreviewAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<GetRaceCollectionReadinessResponse>> GetRaceCollectionReadinessAsync(GetRaceCollectionReadinessRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetRaceEntryOwnerMigrationResponse>> GetRaceEntryOwnerMigrationAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<GetRevisionRecollectionProgressResponse>> GetRevisionRecollectionProgressAsync(GetRevisionRecollectionProgressRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetTaskViewCountsResponse>> GetTaskViewCountsAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<HeartbeatCollectionTaskLeaseResponse>> HeartbeatCollectionTaskLeaseAsync(HeartbeatCollectionTaskLeaseRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ListBackfillBatchesResponse>> ListBackfillBatchesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<ListCollectionTasksResponse>> ListCollectionTasksAsync(ListCollectionTasksRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ListFailureNotificationGroupsResponse>> ListFailureNotificationGroupsAsync(ListFailureNotificationGroupsRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ListFailureNotificationsResponse>> ListFailureNotificationsAsync(ListFailureNotificationsRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ListRaceEntryOwnerRepairCandidatesResponse>> ListRaceEntryOwnerRepairCandidatesAsync(ListRaceEntryOwnerRepairCandidatesRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PreviewCollectionTaskBatchResponse>> PreviewCollectionTaskBatchAsync(PreviewCollectionTaskBatchRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PreviewRaceDetailMigrationResponse>> PreviewRaceDetailMigrationAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<PreviewRaceEntryOwnerMigrationResponse>> PreviewRaceEntryOwnerMigrationAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<PreviewRacePeriodRecollectionResponse>> PreviewRacePeriodRecollectionAsync(PreviewRacePeriodRecollectionRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PreviewRevisionImpactResponse>> PreviewRevisionImpactAsync(PreviewRevisionImpactRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<RecoverBackfillHolesResponse>> RecoverBackfillHolesAsync(RecoverBackfillHolesRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<SearchCollectionStatesResponse>> SearchCollectionStatesAsync(SearchCollectionStatesRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> SetCollectionPipelineAsync(SetCollectionPipelineRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> TransitionCollectionExecutionAsync(TransitionCollectionExecutionRequest request, CancellationToken cancellationToken = default);
}
