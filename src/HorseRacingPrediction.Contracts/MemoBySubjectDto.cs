namespace HorseRacingPrediction.Contracts;

public sealed class MemoBySubjectDto
{
    public string SubjectKey { get; set; } = string.Empty;
    public List<MemoSnapshot> Memos { get; set; } = [];
}