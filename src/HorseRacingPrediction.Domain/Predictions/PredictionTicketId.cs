using EventFlow.Core;

namespace HorseRacingPrediction.Domain.Predictions;

public class PredictionTicketId : Identity<PredictionTicketId>
{
    public PredictionTicketId(string value)
        : base(value)
    {
    }
}
