namespace HorseRacingPrediction.Contracts;

public sealed record ChangeMemoSubjectsRequest(IReadOnlyList<MemoSubjectDto> Subjects);
