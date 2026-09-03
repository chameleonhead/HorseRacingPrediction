namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed class OwnerAliasMappingReadModel
{
    public string NormalizedAlias { get; set; } = string.Empty;
    public string AliasName { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsDisplayName { get; set; }
}

public sealed class OwnerMergeAuditReadModel
{
    public string AuditId { get; set; } = string.Empty;
    public string SourceOwnerId { get; set; } = string.Empty;
    public string TargetOwnerId { get; set; } = string.Empty;
    public string SourceNames { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
