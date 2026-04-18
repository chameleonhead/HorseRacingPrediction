using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Predictions;

public sealed class PredictionTicketCreated : AggregateEvent<PredictionTicketAggregate, PredictionTicketId>
{
    public PredictionTicketCreated(
        string raceId,
        string predictorType,
        string predictorId,
        decimal confidenceScore,
        string? summaryComment)
    {
        RaceId = raceId;
        PredictorType = predictorType;
        PredictorId = predictorId;
        ConfidenceScore = confidenceScore;
        SummaryComment = summaryComment;
        PredictedAt = DateTimeOffset.UtcNow;
    }

    public string RaceId { get; }
    public string PredictorType { get; }
    public string PredictorId { get; }
    public decimal ConfidenceScore { get; }
    public string? SummaryComment { get; }
    public DateTimeOffset PredictedAt { get; }
}

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
