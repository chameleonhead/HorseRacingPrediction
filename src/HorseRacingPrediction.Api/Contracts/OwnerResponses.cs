namespace HorseRacingPrediction.Api.Contracts;

public sealed record OwnerSummaryResponse(
    string OwnerId,
    string DisplayName,
    IReadOnlyList<string> NameVariants,
    int CurrentHorseCount,
    int ParticipationCount,
    DateOnly? LastParticipationDate);

public sealed record OwnerDetailResponse(
    OwnerSummaryResponse Summary,
    IReadOnlyList<RelatedObjectResponse> CurrentHorses,
    IReadOnlyList<RelatedObjectResponse> RelatedTrainers,
    IReadOnlyList<ParticipationHistoryEntryResponse> Participations,
    IReadOnlyList<OwnerMergeAuditResponse> MergeHistory,
    bool HasMoreParticipations = false);

public sealed record RelatedObjectResponse(string ObjectType, string ObjectId, string DisplayName, int RelationshipCount);

public sealed record MergeOwnerRequest(string SourceOwnerId, string Reason);
public sealed record OwnerMergeAuditResponse(string SourceOwnerId, string TargetOwnerId, IReadOnlyList<string> SourceNames, string ActorId, string Reason, DateTimeOffset CreatedAt);
