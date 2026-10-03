namespace HorseRacingPrediction.Contracts.Collection;

/// <summary>
/// A structured public-provider candidate recorded when subject identification is ambiguous.
/// </summary>
public sealed record SubjectIdentificationCandidateDto(string Name, string Url, string? Evidence = null);
