using EventFlow.Core;

namespace HorseRacingPrediction.Domain.Races;

public class RaceId : Identity<RaceId>
{
    public RaceId(string value)
        : base(value)
    {
    }
}
