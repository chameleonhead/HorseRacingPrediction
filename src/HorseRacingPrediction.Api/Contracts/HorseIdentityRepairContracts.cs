namespace HorseRacingPrediction.Api.Contracts;

public sealed record HorseIdentityRepairCandidateResponse(
    string CandidateId, string SourceHorseId, string TargetHorseId, string JraIdentity,
    string RaceId, string EntryId, bool SafeToApply, string? BlockingReason,
    string? SourceHorseName = null, string? TargetHorseName = null, string? RaceName = null,
    int CollectionTasksToDisable = 0);

public sealed record HorseIdentityRepairPreviewResponse(
    string RepairId, IReadOnlyList<HorseIdentityRepairCandidateResponse> Candidates);

public sealed record ApplyHorseIdentityRepairRequest(IReadOnlyList<string> CandidateIds);

public sealed record ApplyHorseIdentityRepairResponse(
    string RepairId, int AppliedCount, int SkippedCount, int DisabledCollectionTaskCount = 0,
    int RunningCancellationRequestCount = 0);
