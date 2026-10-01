namespace HorseRacingPrediction.Contracts.Owners;

public sealed record OwnerIdentityRecoveryCandidateDto(
    Guid NotificationId,
    Guid TaskId,
    string SourceId,
    string? Name,
    string? RaceId,
    string? TargetId,
    string? Fingerprint,
    string? BlockingReason);
