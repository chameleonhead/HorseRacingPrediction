namespace HorseRacingPrediction.Contracts.Repairs;

public sealed record SubjectIdentificationCandidateSelectionInputDto(
    string? Name,
    string? Url,
    string? Evidence = null);
