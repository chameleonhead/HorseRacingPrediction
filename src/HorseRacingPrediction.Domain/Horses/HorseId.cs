using EventFlow.Core;

namespace HorseRacingPrediction.Domain.Horses;

public class HorseId : Identity<HorseId>
{
    public HorseId(string value)
        : base(value)
    {
    }
}
