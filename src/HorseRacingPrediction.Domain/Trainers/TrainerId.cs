using EventFlow.Core;

namespace HorseRacingPrediction.Domain.Trainers;

public class TrainerId : Identity<TrainerId>
{
    public TrainerId(string value)
        : base(value)
    {
    }
}
