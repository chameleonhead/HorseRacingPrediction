namespace HorseRacingPrediction.Contracts.Identity;

public sealed record HorseIdentityComparisonDto(
    string Rule,
    string InputField,
    string EvidenceStatus,
    string? CandidateHorseId,
    string MatchStatus);
