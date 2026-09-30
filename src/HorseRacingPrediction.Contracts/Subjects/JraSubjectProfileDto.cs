
namespace HorseRacingPrediction.Contracts.Subjects;

public sealed record JraSubjectProfileDto(string SubjectType, string Name, string SourceIdentity,
    string SourceUrl, Dictionary<string, string> Fields, DateTimeOffset AcquiredAt);
