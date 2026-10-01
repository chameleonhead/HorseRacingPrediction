namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionResourceDetailDto(CollectionStateSnapshotDto? State,
    IReadOnlyList<ResourceLocationCandidateDto> Locations, IReadOnlyList<CollectionRequestSummaryDto> Requests,
    IReadOnlyList<CollectionTaskSummaryDto> Tasks, IReadOnlyList<CollectionAttemptSummaryDto> Attempts,
    int RequestTotal = 0, int TaskTotal = 0, int AttemptTotal = 0, int HistoryPage = 1,
    int HistoryPageSize = 25, CollectionTaskSummaryDto? LatestTask = null, int? TaskHistoryPage = null,
    int? AttemptHistoryPage = null, IReadOnlyList<CollectionFailureNotificationDto>? Failures = null,
    IReadOnlyList<RaceArtifactSnapshotDto>? RaceArtifacts = null,
    RaceSchedulingEvidenceDto? RaceEvidence = null,
    IReadOnlyList<CollectionAttemptStageSummaryDto>? StageOutcomes = null)
{
    public int RequestHistoryPage => HistoryPage;
    public int EffectiveTaskHistoryPage => TaskHistoryPage ?? HistoryPage;
    public int EffectiveAttemptHistoryPage => AttemptHistoryPage ?? HistoryPage;
}
