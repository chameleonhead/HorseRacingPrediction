namespace HorseRacingPrediction.Contracts.Collection;

public sealed record ResourceLocationCandidateDto(long LocationId, Uri Url, ResourceLocationSource Source,
    ResourceLocationStatus Status, DateTimeOffset? LastVerifiedAt, RaceArtifactKind? Artifact = null);
