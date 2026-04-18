using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Predictions;

public sealed class PredictionTicketState : AggregateState<PredictionTicketAggregate, PredictionTicketId, PredictionTicketState>,
    IApply<PredictionTicketCreated>,
    IApply<PredictionMarkAdded>
{
    private readonly List<PredictionMarkDetails> _marks = new();

    public bool IsCreated { get; private set; }
    public string? RaceId { get; private set; }
    public string? PredictorType { get; private set; }
    public string? PredictorId { get; private set; }
    public decimal ConfidenceScore { get; private set; }
    public string? SummaryComment { get; private set; }
    public DateTimeOffset? PredictedAt { get; private set; }
    public IReadOnlyCollection<PredictionMarkDetails> Marks => _marks.AsReadOnly();

    public void Apply(PredictionTicketCreated aggregateEvent)
    {
        IsCreated = true;
        RaceId = aggregateEvent.RaceId;
        PredictorType = aggregateEvent.PredictorType;
        PredictorId = aggregateEvent.PredictorId;
        ConfidenceScore = aggregateEvent.ConfidenceScore;
        SummaryComment = aggregateEvent.SummaryComment;
        PredictedAt = aggregateEvent.PredictedAt;
    }

    public void Apply(PredictionMarkAdded aggregateEvent)
    {
        _marks.Add(new PredictionMarkDetails(
            aggregateEvent.EntryId,
            aggregateEvent.MarkCode,
            aggregateEvent.PredictedRank,
            aggregateEvent.Score,
            aggregateEvent.Comment));
    }
}
