
namespace HorseRacingPrediction.Contracts.Memos;

public sealed class MemoBySubjectDto
{
    public string SubjectKey { get; set; } = string.Empty;
    public List<MemoDto> Memos { get; set; } = [];
}
