using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Predictions;

public sealed class PredictionRationaleAdded : AggregateEvent<PredictionTicketAggregate, PredictionTicketId>
{
    public PredictionRationaleAdded(string subjectType, string subjectId, string signalType,
        string? signalValue = null, string? explanationText = null)
    {
        SubjectType = subjectType;
        SubjectId = subjectId;
        SignalType = signalType;
        SignalValue = signalValue;
        ExplanationText = explanationText;
    }

    public string SubjectType { get; }
    public string SubjectId { get; }
    public string SignalType { get; }
    public string? SignalValue { get; }
    public string? ExplanationText { get; }
}
