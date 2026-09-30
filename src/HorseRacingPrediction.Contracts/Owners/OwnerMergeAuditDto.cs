
namespace HorseRacingPrediction.Contracts.Owners;

public sealed record OwnerMergeAuditDto(string SourceOwnerId, string TargetOwnerId, IReadOnlyList<string> SourceNames, string ActorId, string Reason, DateTimeOffset CreatedAt);
