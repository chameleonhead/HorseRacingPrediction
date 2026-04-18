using EventFlow.Core;

namespace HorseRacingPrediction.Domain.Memos;

public class MemoId : Identity<MemoId>
{
    public MemoId(string value)
        : base(value)
    {
    }
}
