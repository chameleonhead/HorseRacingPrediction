using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Predictions;

public sealed class PredictionTicketState : AggregateState<PredictionTicketAggregate, PredictionTicketId, PredictionTicketState>,
    IApply<PredictionTicketCreated>,
    IApply<PredictionMarkAdded>,
    IApply<BettingSuggestionAdded>,
    IApply<PredictionRationaleAdded>,
    IApply<PredictionTicketFinalized>,
    IApply<PredictionTicketWithdrawn>,
    IApply<PredictionTicketEvaluated>,
    IApply<PredictionEvaluationRecalculated>,
    IApply<PredictionMetadataCorrected>
{
    private readonly List<PredictionMarkDetails> _marks = new();
    private readonly List<BettingSuggestionDetails> _bettingSuggestions = new();
    private readonly List<PredictionRationaleDetails> _rationales = new();
    private readonly List<PredictionEvaluationDetails> _evaluations = new();

    public bool IsCreated { get; private set; }
    public string? RaceId { get; private set; }
    public string? PredictorType { get; private set; }
    public string? PredictorId { get; private set; }
    public decimal ConfidenceScore { get; private set; }
    public string? SummaryComment { get; private set; }
    public DateTimeOffset? PredictedAt { get; private set; }
    public TicketStatus TicketStatus { get; private set; } = TicketStatus.Draft;
    public IReadOnlyCollection<PredictionMarkDetails> Marks => _marks.AsReadOnly();
    public IReadOnlyCollection<BettingSuggestionDetails> BettingSuggestions => _bettingSuggestions.AsReadOnly();
    public IReadOnlyCollection<PredictionRationaleDetails> Rationales => _rationales.AsReadOnly();
    public IReadOnlyCollection<PredictionEvaluationDetails> Evaluations => _evaluations.AsReadOnly();

    public void Apply(PredictionTicketCreated e)
    {
        IsCreated = true;
        RaceId = e.RaceId;
        PredictorType = e.PredictorType;
        PredictorId = e.PredictorId;
        ConfidenceScore = e.ConfidenceScore;
        SummaryComment = e.SummaryComment;
        PredictedAt = e.PredictedAt;
        TicketStatus = TicketStatus.Draft;
    }

    public void Apply(PredictionMarkAdded e)
    {
        _marks.Add(new PredictionMarkDetails(
            e.EntryId, e.MarkCode, e.PredictedRank, e.Score, e.Comment));
    }

    public void Apply(BettingSuggestionAdded e)
    {
        _bettingSuggestions.Add(new BettingSuggestionDetails(
            e.BetTypeCode, e.SelectionExpression, e.StakeAmount, e.ExpectedValue));
    }

    public void Apply(PredictionRationaleAdded e)
    {
        _rationales.Add(new PredictionRationaleDetails(
            e.SubjectType, e.SubjectId, e.SignalType, e.SignalValue, e.ExplanationText));
    }

    public void Apply(PredictionTicketFinalized e)
    {
        TicketStatus = TicketStatus.Finalized;
    }

    public void Apply(PredictionTicketWithdrawn e)
    {
        TicketStatus = TicketStatus.Withdrawn;
    }

    public void Apply(PredictionTicketEvaluated e)
    {
        _evaluations.Add(new PredictionEvaluationDetails(
            e.RaceId, e.EvaluatedAt, e.EvaluationRevision, e.HitTypeCodes,
            e.ScoreSummary, e.ReturnAmount, e.Roi));
    }

    public void Apply(PredictionEvaluationRecalculated e)
    {
        _evaluations.Add(new PredictionEvaluationDetails(
            e.RaceId, e.EvaluatedAt, e.EvaluationRevision, e.HitTypeCodes,
            e.ScoreSummary, e.ReturnAmount, e.Roi));
    }

    public void Apply(PredictionMetadataCorrected e)
    {
        if (e.ConfidenceScore.HasValue) ConfidenceScore = e.ConfidenceScore.Value;
        if (e.SummaryComment != null) SummaryComment = e.SummaryComment;
    }
}
