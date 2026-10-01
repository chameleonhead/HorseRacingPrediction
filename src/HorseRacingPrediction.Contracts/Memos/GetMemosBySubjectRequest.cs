using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Memos;

public sealed record GetMemosBySubjectRequest(
    [property: JsonIgnore] string SubjectType,
    [property: JsonIgnore] string SubjectId);
