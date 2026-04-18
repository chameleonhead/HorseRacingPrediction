using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Predictions;

public class PredictionTicketAggregate : AggregateRoot<PredictionTicketAggregate, PredictionTicketId>,
    IEmit<PredictionTicketCreated>,
    IEmit<PredictionMarkAdded>,
    IEmit<BettingSuggestionAdded>,
    IEmit<PredictionRationaleAdded>,
    IEmit<PredictionTicketFinalized>,
    IEmit<PredictionTicketWithdrawn>,
    IEmit<PredictionTicketEvaluated>,
    IEmit<PredictionEvaluationRecalculated>,
    IEmit<PredictionMetadataCorrected>
{
    private readonly PredictionTicketState _state = new();

    public PredictionTicketAggregate(PredictionTicketId id)
        : base(id)
    {
        Register(_state);
    }

    public void Create(
        string raceId,
        string predictorType,
        string predictorId,
        decimal confidenceScore,
        string? summaryComment)
    {
        if (_state.IsCreated)
            throw new InvalidOperationException("Prediction ticket is already created.");

        Emit(new PredictionTicketCreated(raceId, predictorType, predictorId, confidenceScore, summaryComment));
    }

    public void AddMark(string entryId, string markCode, int predictedRank, decimal score, string? comment)
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Prediction ticket is not created.");

        if (_state.TicketStatus == TicketStatus.Finalized)
            throw new InvalidOperationException("Cannot add marks to a finalized ticket.");

        if (_state.TicketStatus == TicketStatus.Withdrawn)
            throw new InvalidOperationException("Cannot add marks to a withdrawn ticket.");

        Emit(new PredictionMarkAdded(entryId, markCode, predictedRank, score, comment));
    }

    public void AddBettingSuggestion(string betTypeCode, string selectionExpression,
        decimal? stakeAmount = null, decimal? expectedValue = null)
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Prediction ticket is not created.");

        if (_state.TicketStatus == TicketStatus.Finalized)
            throw new InvalidOperationException("Cannot add suggestions to a finalized ticket.");

        if (_state.TicketStatus == TicketStatus.Withdrawn)
            throw new InvalidOperationException("Cannot add suggestions to a withdrawn ticket.");

        Emit(new BettingSuggestionAdded(betTypeCode, selectionExpression, stakeAmount, expectedValue));
    }

    public void AddRationale(string subjectType, string subjectId, string signalType,
        string? signalValue = null, string? explanationText = null)
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Prediction ticket is not created.");

        if (_state.TicketStatus == TicketStatus.Withdrawn)
            throw new InvalidOperationException("Cannot add rationale to a withdrawn ticket.");

        Emit(new PredictionRationaleAdded(subjectType, subjectId, signalType, signalValue, explanationText));
    }

    public void FinalizeTicket()
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Prediction ticket is not created.");

        if (_state.TicketStatus != TicketStatus.Draft)
            throw new InvalidOperationException("Only draft tickets can be finalized.");

        Emit(new PredictionTicketFinalized());
    }

    public void Withdraw(string? reason = null)
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Prediction ticket is not created.");

        if (_state.TicketStatus == TicketStatus.Withdrawn)
            throw new InvalidOperationException("Ticket is already withdrawn.");

        Emit(new PredictionTicketWithdrawn(reason));
    }

    public void Evaluate(string raceId, DateTimeOffset evaluatedAt,
        int evaluationRevision, IReadOnlyList<string> hitTypeCodes,
        decimal? scoreSummary = null, decimal? returnAmount = null, decimal? roi = null)
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Prediction ticket is not created.");

        Emit(new PredictionTicketEvaluated(raceId, evaluatedAt, evaluationRevision, hitTypeCodes,
            scoreSummary, returnAmount, roi));
    }

    public void RecalculateEvaluation(string raceId, DateTimeOffset evaluatedAt,
        int evaluationRevision, IReadOnlyList<string> hitTypeCodes,
        decimal? scoreSummary = null, decimal? returnAmount = null, decimal? roi = null)
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Prediction ticket is not created.");

        Emit(new PredictionEvaluationRecalculated(raceId, evaluatedAt, evaluationRevision, hitTypeCodes,
            scoreSummary, returnAmount, roi));
    }

    public void CorrectMetadata(decimal? confidenceScore = null, string? summaryComment = null, string? reason = null)
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Prediction ticket is not created.");

        Emit(new PredictionMetadataCorrected(confidenceScore, summaryComment, reason));
    }

    public PredictionTicketDetails GetDetails()
    {
        return new PredictionTicketDetails(
            Id.Value,
            _state.RaceId,
            _state.PredictorType,
            _state.PredictorId,
            _state.ConfidenceScore,
            _state.SummaryComment,
            _state.PredictedAt,
            _state.TicketStatus,
            _state.Marks,
            _state.BettingSuggestions,
            _state.Rationales,
            _state.Evaluations);
    }

    public void Apply(PredictionTicketCreated e) { }
    public void Apply(PredictionMarkAdded e) { }
    public void Apply(BettingSuggestionAdded e) { }
    public void Apply(PredictionRationaleAdded e) { }
    public void Apply(PredictionTicketFinalized e) { }
    public void Apply(PredictionTicketWithdrawn e) { }
    public void Apply(PredictionTicketEvaluated e) { }
    public void Apply(PredictionEvaluationRecalculated e) { }
    public void Apply(PredictionMetadataCorrected e) { }
}
