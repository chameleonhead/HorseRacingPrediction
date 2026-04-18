using EventFlow.Commands;
using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Application.Commands.Predictions;

public sealed class CreatePredictionTicketCommand : Command<PredictionTicketAggregate, PredictionTicketId>
{
    public CreatePredictionTicketCommand(
        PredictionTicketId aggregateId,
        string raceId,
        string predictorType,
        string predictorId,
        decimal confidenceScore,
        string? summaryComment)
        : base(aggregateId)
    {
        RaceId = raceId;
        PredictorType = predictorType;
        PredictorId = predictorId;
        ConfidenceScore = confidenceScore;
        SummaryComment = summaryComment;
    }

    public string RaceId { get; }
    public string PredictorType { get; }
    public string PredictorId { get; }
    public decimal ConfidenceScore { get; }
    public string? SummaryComment { get; }
}
