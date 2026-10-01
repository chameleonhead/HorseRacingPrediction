
using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Memos;

public sealed record ChangeMemoSubjectsRequest(
    [property: JsonIgnore] string MemoId,
    IReadOnlyList<MemoSubjectDto>? Subjects);
