namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed class HorseIdentityRepairCandidateReadModel
{
    public string CandidateId { get; set; } = string.Empty;
    public string RepairId { get; set; } = string.Empty;
    public string SourceHorseId { get; set; } = string.Empty;
    public string TargetHorseId { get; set; } = string.Empty;
    public string JraIdentity { get; set; } = string.Empty;
    public string RaceId { get; set; } = string.Empty;
    public string EntryId { get; set; } = string.Empty;
    public DateTimeOffset DetectedAt { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
}

public sealed class HorseIdentityRepairRedirectReadModel
{
    public string SourceHorseId { get; set; } = string.Empty;
    public string TargetHorseId { get; set; } = string.Empty;
    public string RepairId { get; set; } = string.Empty;
    public string JraIdentity { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
