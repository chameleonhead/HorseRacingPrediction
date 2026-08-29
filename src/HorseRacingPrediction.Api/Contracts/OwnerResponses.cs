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
    IReadOnlyList<ParticipationHistoryEntryResponse> Participations);

public sealed record RelatedObjectResponse(string ObjectType, string ObjectId, string DisplayName, int RelationshipCount);
