namespace HorseRacingPrediction.Contracts;

public sealed record OwnerSummaryDto(
    string OwnerId,
    string DisplayName,
    IReadOnlyList<string> NameVariants,
    int CurrentHorseCount,
    int ParticipationCount,
    DateOnly? LastParticipationDate);

public sealed record OwnerDetailDto(
    OwnerSummaryDto Summary,
    IReadOnlyList<RelatedObjectDto> CurrentHorses,
    IReadOnlyList<RelatedObjectDto> RelatedTrainers,
    IReadOnlyList<ParticipationHistoryEntryDto> Participations,
    IReadOnlyList<OwnerMergeAuditDto> MergeHistory,
    bool HasMoreParticipations = false,
    IReadOnlyList<RelationshipSummaryDto>? TopHorses = null);

public sealed record RelatedObjectDto(string ObjectType, string ObjectId, string DisplayName, int RelationshipCount);

public sealed record MergeOwnerRequest(string SourceOwnerId, string Reason);
public sealed record UpdateOwnerRequest(string DisplayName, string Reason, IReadOnlyList<string>? NameVariants = null);
public sealed record OwnerMergeAuditDto(string SourceOwnerId, string TargetOwnerId, IReadOnlyList<string> SourceNames, string ActorId, string Reason, DateTimeOffset CreatedAt);
