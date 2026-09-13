namespace HorseRacingPrediction.Api.Contracts;

public sealed record HorseIdentityRepairCandidateResponse(
    string CandidateId, string SourceHorseId, string TargetHorseId, string JraIdentity,
    string RaceId, string EntryId, bool SafeToApply, string? BlockingReason);

public sealed record HorseIdentityRepairPreviewResponse(
    string RepairId, IReadOnlyList<HorseIdentityRepairCandidateResponse> Candidates);

public sealed record ApplyHorseIdentityRepairRequest(IReadOnlyList<string> CandidateIds);

public sealed record ApplyHorseIdentityRepairResponse(string RepairId, int AppliedCount, int SkippedCount);
