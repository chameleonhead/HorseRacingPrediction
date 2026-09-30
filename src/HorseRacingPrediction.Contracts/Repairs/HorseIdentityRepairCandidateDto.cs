
namespace HorseRacingPrediction.Contracts.Repairs;

public sealed record HorseIdentityRepairCandidateDto(
    string CandidateId, string SourceHorseId, string TargetHorseId, string JraIdentity,
    string RaceId, string EntryId, bool SafeToApply, string? BlockingReason,
    string? SourceHorseName = null, string? TargetHorseName = null, string? RaceName = null,
    int CollectionTasksToDisable = 0);
