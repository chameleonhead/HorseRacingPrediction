using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Predictions;

public sealed class PredictionMarkAdded : AggregateEvent<PredictionTicketAggregate, PredictionTicketId>
{
    public PredictionMarkAdded(string entryId, string markCode, int predictedRank, decimal score, string? comment)
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
