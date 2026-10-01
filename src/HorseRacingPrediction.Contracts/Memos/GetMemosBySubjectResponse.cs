namespace HorseRacingPrediction.Contracts.Memos;

public sealed record GetMemosBySubjectResponse(IReadOnlyList<MemoDto> Memos);
