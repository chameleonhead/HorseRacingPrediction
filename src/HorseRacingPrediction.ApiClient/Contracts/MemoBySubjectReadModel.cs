namespace HorseRacingPrediction.ApiClient.Contracts;

public sealed class MemoBySubjectReadModel
{
    public string SubjectKey { get; set; } = string.Empty;
    public List<MemoSnapshot> Memos { get; set; } = [];
}