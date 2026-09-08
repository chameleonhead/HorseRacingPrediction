namespace HorseRacingPrediction.Scraping.Jra.Models;

public sealed record JraSubjectIdentity(string SubjectType, string Name, DateOnly? BirthDate = null, string? SourceIdentity = null);
