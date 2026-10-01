using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Subjects;

public sealed record GetSubjectProfileRequest(
    [property: JsonIgnore] string Kind,
    [property: JsonIgnore] string SubjectId);
