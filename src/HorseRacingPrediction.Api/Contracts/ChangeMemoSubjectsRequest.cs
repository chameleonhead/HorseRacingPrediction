namespace HorseRacingPrediction.Api.Contracts;

public sealed record ChangeMemoSubjectsRequest(IReadOnlyList<MemoSubjectDto> Subjects);
