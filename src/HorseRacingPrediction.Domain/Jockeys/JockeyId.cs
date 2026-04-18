using EventFlow.Core;

namespace HorseRacingPrediction.Domain.Jockeys;

public class JockeyId : Identity<JockeyId>
{
    public JockeyId(string value)
        : base(value)
    {
    }
}
