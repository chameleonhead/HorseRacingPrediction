namespace HorseRacingPrediction.Contracts.Identity;

public sealed record HorseSourceEvidenceDto(
    string Origin,
    string Status,
    string? SourceIdentity,
    string? NormalizedSourceIdentity,
    string SourceUrlStatus,
    string? SourceUrlHorseIdentity);
