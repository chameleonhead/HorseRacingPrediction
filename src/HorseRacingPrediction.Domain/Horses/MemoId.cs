using EventFlow.Core;

namespace HorseRacingPrediction.Domain.Horses;

public class MemoId : Identity<MemoId>
{
    public MemoId(string value)
        : base(value)
    {
    }
}
