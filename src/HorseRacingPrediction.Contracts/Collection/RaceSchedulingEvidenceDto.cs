namespace HorseRacingPrediction.Contracts.Collection;

public sealed record RaceSchedulingEvidenceDto(DateTimeOffset? OfficialStartAt, string? Provenance,
    DateTimeOffset? VerifiedAt);
