using EventFlow.Aggregates;
using EventFlow.ReadStores;
using HorseRacingPrediction.Domain.Predictions;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public class PredictionComparisonViewReadModel : IReadModel,
    IAmReadModelFor<RaceAggregate, RaceId, RaceCreated>,
    IAmReadModelFor<RaceAggregate, RaceId, RaceDataCorrected>,
    IAmReadModelFor<RaceAggregate, RaceId, EntryRegistered>,
    IAmReadModelFor<RaceAggregate, RaceId, RaceResultDeclared>,
    IAmReadModelFor<RaceAggregate, RaceId, EntryResultDeclared>,
    IAmReadModelFor<RaceAggregate, RaceId, PayoutResultDeclared>,
    IAmReadModelFor<PredictionTicketAggregate, PredictionTicketId, PredictionTicketCreated>,
    IAmReadModelFor<PredictionTicketAggregate, PredictionTicketId, PredictionMarkAdded>,
    IAmReadModelFor<PredictionTicketAggregate, PredictionTicketId, PredictionTicketFinalized>,
    IAmReadModelFor<PredictionTicketAggregate, PredictionTicketId, PredictionTicketWithdrawn>,
    IAmReadModelFor<PredictionTicketAggregate, PredictionTicketId, PredictionTicketEvaluated>,
    IAmReadModelFor<PredictionTicketAggregate, PredictionTicketId, PredictionEvaluationRecalculated>,
    IAmReadModelFor<PredictionTicketAggregate, PredictionTicketId, PredictionMetadataCorrected>
{
    private readonly List<EntryResultSnapshot> _entryResults = new();
    private readonly Dictionary<string, (string HorseId, int HorseNumber)> _entryIndex = new();
    private readonly Dictionary<string, MutableTicketState> _ticketStates = new();

    public string RaceId { get; private set; } = string.Empty;
    public string? RaceName { get; private set; }
    public string? WinningHorseName { get; private set; }
    public DateTimeOffset? ResultDeclaredAt { get; private set; }
    public IReadOnlyList<PredictionTicketSnapshot> PredictionTickets =>
        _ticketStates.Values.Select(s => s.ToSnapshot()).ToList().AsReadOnly();
    public IReadOnlyList<EntryResultSnapshot> EntryResults => _entryResults.AsReadOnly();
    public PayoutResultSnapshot? PayoutResult { get; private set; }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceCreated> domainEvent,
        CancellationToken cancellationToken)
    {
        RaceId = domainEvent.AggregateIdentity.Value;
        RaceName = domainEvent.AggregateEvent.RaceName;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceDataCorrected> domainEvent,
        CancellationToken cancellationToken)
    {
        if (domainEvent.AggregateEvent.RaceName != null) RaceName = domainEvent.AggregateEvent.RaceName;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, EntryRegistered> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        _entryIndex[e.EntryId] = (e.HorseId, e.HorseNumber);
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceResultDeclared> domainEvent,
        CancellationToken cancellationToken)
    {
        WinningHorseName = domainEvent.AggregateEvent.WinningHorseName;
        ResultDeclaredAt = domainEvent.AggregateEvent.DeclaredAt;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, EntryResultDeclared> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        _entryIndex.TryGetValue(e.EntryId, out var entryInfo);
        _entryResults.Add(new EntryResultSnapshot(
            e.EntryId,
            entryInfo.HorseId ?? string.Empty,
            entryInfo.HorseNumber,
            e.FinishPosition, e.OfficialTime,
            e.MarginText, e.LastThreeFurlongTime,
            e.AbnormalResultCode, e.PrizeMoney));
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, PayoutResultDeclared> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        PayoutResult = new PayoutResultSnapshot(
            e.DeclaredAt,
            e.WinPayouts.Select(p => new PayoutEntrySnapshot(p.Combination, p.Amount)).ToList(),
            e.PlacePayouts.Select(p => new PayoutEntrySnapshot(p.Combination, p.Amount)).ToList(),
            e.QuinellaPayouts.Select(p => new PayoutEntrySnapshot(p.Combination, p.Amount)).ToList(),
            e.ExactaPayouts.Select(p => new PayoutEntrySnapshot(p.Combination, p.Amount)).ToList(),
            e.TrifectaPayouts.Select(p => new PayoutEntrySnapshot(p.Combination, p.Amount)).ToList());
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<PredictionTicketAggregate, PredictionTicketId, PredictionTicketCreated> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        var ticketId = domainEvent.AggregateIdentity.Value;
        _ticketStates[ticketId] = new MutableTicketState
        {
            PredictionTicketId = ticketId,
            PredictorType = e.PredictorType,
            PredictorId = e.PredictorId,
            ConfidenceScore = e.ConfidenceScore,
            SummaryComment = e.SummaryComment,
            PredictedAt = e.PredictedAt
        };
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<PredictionTicketAggregate, PredictionTicketId, PredictionMarkAdded> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        var ticketId = domainEvent.AggregateIdentity.Value;
        if (_ticketStates.TryGetValue(ticketId, out var state))
        {
            state.Marks.Add(new PredictionMarkSnapshot(
                e.EntryId, e.MarkCode, e.PredictedRank, e.Score, e.Comment));
        }
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<PredictionTicketAggregate, PredictionTicketId, PredictionTicketFinalized> domainEvent,
        CancellationToken cancellationToken)
    {
        var ticketId = domainEvent.AggregateIdentity.Value;
        if (_ticketStates.TryGetValue(ticketId, out var state))
            state.Status = TicketStatus.Finalized;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<PredictionTicketAggregate, PredictionTicketId, PredictionTicketWithdrawn> domainEvent,
        CancellationToken cancellationToken)
    {
        var ticketId = domainEvent.AggregateIdentity.Value;
        if (_ticketStates.TryGetValue(ticketId, out var state))
            state.Status = TicketStatus.Withdrawn;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<PredictionTicketAggregate, PredictionTicketId, PredictionTicketEvaluated> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        var ticketId = domainEvent.AggregateIdentity.Value;
        if (_ticketStates.TryGetValue(ticketId, out var state))
        {
            state.Evaluations.Add(new PredictionEvaluationSnapshot(
                e.EvaluatedAt, e.EvaluationRevision, e.HitTypeCodes,
                e.ScoreSummary, e.ReturnAmount, e.Roi));
            state.EvaluationStatus = EvaluationStatus.Ready;
        }
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<PredictionTicketAggregate, PredictionTicketId, PredictionEvaluationRecalculated> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        var ticketId = domainEvent.AggregateIdentity.Value;
        if (_ticketStates.TryGetValue(ticketId, out var state))
        {
            state.Evaluations.Add(new PredictionEvaluationSnapshot(
                e.EvaluatedAt, e.EvaluationRevision, e.HitTypeCodes,
                e.ScoreSummary, e.ReturnAmount, e.Roi));
        }
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<PredictionTicketAggregate, PredictionTicketId, PredictionMetadataCorrected> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        var ticketId = domainEvent.AggregateIdentity.Value;
        if (_ticketStates.TryGetValue(ticketId, out var state))
        {
            if (e.ConfidenceScore.HasValue) state.ConfidenceScore = e.ConfidenceScore.Value;
            if (e.SummaryComment != null) state.SummaryComment = e.SummaryComment;
        }
        return Task.CompletedTask;
    }

    private sealed class MutableTicketState
    {
        public string PredictionTicketId { get; set; } = string.Empty;
        public string PredictorType { get; set; } = string.Empty;
        public string PredictorId { get; set; } = string.Empty;
        public TicketStatus Status { get; set; } = TicketStatus.Draft;
        public decimal ConfidenceScore { get; set; }
        public string? SummaryComment { get; set; }
        public DateTimeOffset PredictedAt { get; set; }
        public List<PredictionMarkSnapshot> Marks { get; set; } = new();
        public List<PredictionEvaluationSnapshot> Evaluations { get; set; } = new();
        public EvaluationStatus EvaluationStatus { get; set; } = EvaluationStatus.Ready;

        public PredictionTicketSnapshot ToSnapshot() => new(
            PredictionTicketId, PredictorType, PredictorId, Status, ConfidenceScore, SummaryComment, PredictedAt,
            Marks.AsReadOnly(),
            Evaluations.OrderByDescending(e => e.EvaluationRevision).FirstOrDefault(),
            EvaluationStatus);
    }
}
