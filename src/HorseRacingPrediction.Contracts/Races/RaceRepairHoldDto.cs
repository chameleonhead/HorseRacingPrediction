namespace HorseRacingPrediction.Contracts.Races;

public sealed record RaceRepairHoldDto(string RaceId, string OperationId, long Generation, string Reason,
    DateTimeOffset CreatedAt, DateTimeOffset? ReleasedAt, string? AssignmentFingerprint,
    int ReadyTasks, int RunningTasks, IReadOnlyList<string> Blockers, int RequiredRevision,
    int UnresolvedLeases, IReadOnlyList<string> Aliases, IReadOnlyList<string> Definitions)
{
    public bool IsActive => ReleasedAt is null;
    public bool IsQuiescent => IsActive && RunningTasks == 0 && UnresolvedLeases == 0 && Blockers.Count == 0;
}
