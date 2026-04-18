using EventFlow.Commands;
using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Application.Commands.Predictions;

public sealed class AddPredictionMarkCommand : Command<PredictionTicketAggregate, PredictionTicketId>
{
    public AddPredictionMarkCommand(
        PredictionTicketId aggregateId,
        string entryId,
        string markCode,
        int predictedRank,
        decimal score,
        string? comment)
        : base(aggregateId)
    {
        EntryId = entryId;
        MarkCode = markCode;
        PredictedRank = predictedRank;
        Score = score;
        Comment = comment;
    }

    public string EntryId { get; }
    public string MarkCode { get; }
    public int PredictedRank { get; }
    public decimal Score { get; }
    public string? Comment { get; }
}
