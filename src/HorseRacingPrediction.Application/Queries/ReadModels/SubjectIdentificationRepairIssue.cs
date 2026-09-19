namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed class SubjectIdentificationRepairIssue
{
    public Guid IssueId { get; set; }
    public string SubjectType { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
    public string DefinitionId { get; set; } = string.Empty;
    public string? RequestedByRaceId { get; set; }
    public string? SourceIdentity { get; set; }
    public string? SourceUrl { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string ReasonMessage { get; set; } = string.Empty;
    public string EvidenceFingerprint { get; set; } = string.Empty;
    public int Occurrence { get; set; } = 1;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? TargetSubjectId { get; set; }
    public Guid? RecoveryTaskId { get; set; }
}
