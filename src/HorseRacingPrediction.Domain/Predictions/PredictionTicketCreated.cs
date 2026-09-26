using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Predictions;

public sealed class PredictionTicketCreated : AggregateEvent<PredictionTicketAggregate, PredictionTicketId>
{
    public PredictionTicketCreated(
        string raceId,
        string predictorType,
        string predictorId,
        decimal confidenceScore,
        string? summaryComment,
        string? entryAssignmentFingerprint = null)
    {
        RaceId = raceId;
        PredictorType = predictorType;
        PredictorId = predictorId;
        ConfidenceScore = confidenceScore;
        SummaryComment = summaryComment;
        EntryAssignmentFingerprint = entryAssignmentFingerprint;
        PredictedAt = HorseRacingPrediction.Domain.Time.JstClock.Now;
    }

    public string RaceId { get; }
    public string PredictorType { get; }
    public string PredictorId { get; }
    public decimal ConfidenceScore { get; }
    public string? SummaryComment { get; }
    public string? EntryAssignmentFingerprint { get; }
    public DateTimeOffset PredictedAt { get; }
}
