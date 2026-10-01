using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Subjects;

public sealed record PutSubjectProfileRequest(
    [property: JsonIgnore] string Kind,
    [property: JsonIgnore] string SubjectId,
    JraSubjectProfileDto? Profile);
