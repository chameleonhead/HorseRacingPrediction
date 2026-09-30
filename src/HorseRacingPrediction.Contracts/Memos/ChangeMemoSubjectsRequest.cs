
namespace HorseRacingPrediction.Contracts.Memos;

public sealed record ChangeMemoSubjectsRequest(IReadOnlyList<MemoSubjectDto> Subjects);
