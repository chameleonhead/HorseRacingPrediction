using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Predictions;

public class PredictionTicketAggregate : AggregateRoot<PredictionTicketAggregate, PredictionTicketId>,
    IEmit<PredictionTicketCreated>,
    IEmit<PredictionMarkAdded>
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
        {
            throw new InvalidOperationException("Prediction ticket is already created.");
        }

        Emit(new PredictionTicketCreated(raceId, predictorType, predictorId, confidenceScore, summaryComment));
    }

    public void AddMark(string entryId, string markCode, int predictedRank, decimal score, string? comment)
    {
        if (!_state.IsCreated)
        {
            throw new InvalidOperationException("Prediction ticket is not created.");
        }

        Emit(new PredictionMarkAdded(entryId, markCode, predictedRank, score, comment));
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
            _state.Marks);
    }

    public void Apply(PredictionTicketCreated aggregateEvent)
    {
    }

    public void Apply(PredictionMarkAdded aggregateEvent)
    {
    }
}

public sealed record PredictionTicketDetails(
    string PredictionTicketId,
    string? RaceId,
    string? PredictorType,
    string? PredictorId,
    decimal ConfidenceScore,
    string? SummaryComment,
    DateTimeOffset? PredictedAt,
    IReadOnlyCollection<PredictionMarkDetails> Marks);
