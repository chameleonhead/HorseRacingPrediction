using EventFlow.Commands;
using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Application.Commands.Predictions;

public sealed class AddPredictionRationaleCommand : Command<PredictionTicketAggregate, PredictionTicketId>
{
    public AddPredictionRationaleCommand(PredictionTicketId aggregateId,
        string subjectType, string subjectId, string signalType,
        string? signalValue = null, string? explanationText = null)
        : base(aggregateId)
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
