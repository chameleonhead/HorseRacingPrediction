using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.Contracts;

public sealed record SubjectIdentificationRepairCandidateResponse(
    Guid NotificationId,
    Guid TaskId,
    ResourceType SubjectType,
    string SubjectId,
    string DefinitionId,
    string? ErrorMessage,
    DateTimeOffset FailedAt,
    string Evaluation,
    bool SafeToExecute,
    string? BlockingReason,
    string? SuggestedUrl,
    string? HorseMergeCandidateId = null,
    string? MergeTargetId = null);

public sealed record SubjectIdentificationRepairPreviewResponse(
    IReadOnlyList<SubjectIdentificationRepairCandidateResponse> Candidates);

public sealed record ExecuteSubjectIdentificationRepairItem(Guid NotificationId, string? CorrectionUrl = null);

public sealed record ExecuteSubjectIdentificationRepairRequest(
    IReadOnlyList<ExecuteSubjectIdentificationRepairItem> Items);

public sealed record ExecuteSubjectIdentificationRepairResponse(
    int SelectedCount, int CreatedTaskCount, int ReusedTaskCount, IReadOnlyList<Guid> TaskIds,
    int MergedCount = 0, int DisabledCollectionTaskCount = 0, int RunningCancellationRequestCount = 0);

public sealed record DismissSubjectIdentificationFailuresRequest(IReadOnlyList<Guid> NotificationIds);

public sealed record DismissSubjectIdentificationFailuresResponse(
    int SelectedCount, int DismissedCount, int AlreadyClosedCount);
