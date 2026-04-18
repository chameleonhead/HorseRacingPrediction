using EventFlow.Aggregates;
using EventFlow.ReadStores;
using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public class PredictionTicketReadModel : IReadModel,
    IAmReadModelFor<PredictionTicketAggregate, PredictionTicketId, PredictionTicketCreated>,
    IAmReadModelFor<PredictionTicketAggregate, PredictionTicketId, PredictionMarkAdded>,
    IAmReadModelFor<PredictionTicketAggregate, PredictionTicketId, BettingSuggestionAdded>,
    IAmReadModelFor<PredictionTicketAggregate, PredictionTicketId, PredictionRationaleAdded>,
    IAmReadModelFor<PredictionTicketAggregate, PredictionTicketId, PredictionTicketFinalized>,
    IAmReadModelFor<PredictionTicketAggregate, PredictionTicketId, PredictionTicketWithdrawn>,
    IAmReadModelFor<PredictionTicketAggregate, PredictionTicketId, PredictionTicketEvaluated>,
    IAmReadModelFor<PredictionTicketAggregate, PredictionTicketId, PredictionEvaluationRecalculated>,
    IAmReadModelFor<PredictionTicketAggregate, PredictionTicketId, PredictionMetadataCorrected>
{
    private readonly List<PredictionMarkSnapshot> _marks = new();
    private readonly List<PredictionEvaluationSnapshot> _evaluations = new();

    public string PredictionTicketId { get; private set; } = string.Empty;
    public string? RaceId { get; private set; }
    public string? PredictorType { get; private set; }
    public string? PredictorId { get; private set; }
    public decimal ConfidenceScore { get; private set; }
    public string? SummaryComment { get; private set; }
    public DateTimeOffset? PredictedAt { get; private set; }
    public TicketStatus TicketStatus { get; private set; } = TicketStatus.Draft;
    public IReadOnlyList<PredictionMarkSnapshot> Marks => _marks.AsReadOnly();
    public PredictionEvaluationSnapshot? LatestEvaluation =>
        _evaluations.OrderByDescending(e => e.EvaluationRevision).FirstOrDefault();
    public EvaluationStatus EvaluationStatus { get; private set; } = EvaluationStatus.Ready;

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<PredictionTicketAggregate, PredictionTicketId, PredictionTicketCreated> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        PredictionTicketId = domainEvent.AggregateIdentity.Value;
        RaceId = e.RaceId;
        PredictorType = e.PredictorType;
        PredictorId = e.PredictorId;
        ConfidenceScore = e.ConfidenceScore;
        SummaryComment = e.SummaryComment;
        PredictedAt = e.PredictedAt;
        TicketStatus = TicketStatus.Draft;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<PredictionTicketAggregate, PredictionTicketId, PredictionMarkAdded> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        _marks.Add(new PredictionMarkSnapshot(e.EntryId, e.MarkCode, e.PredictedRank, e.Score, e.Comment));
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<PredictionTicketAggregate, PredictionTicketId, BettingSuggestionAdded> domainEvent,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<PredictionTicketAggregate, PredictionTicketId, PredictionRationaleAdded> domainEvent,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<PredictionTicketAggregate, PredictionTicketId, PredictionTicketFinalized> domainEvent,
        CancellationToken cancellationToken)
    {
        TicketStatus = TicketStatus.Finalized;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<PredictionTicketAggregate, PredictionTicketId, PredictionTicketWithdrawn> domainEvent,
        CancellationToken cancellationToken)
    {
        TicketStatus = TicketStatus.Withdrawn;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<PredictionTicketAggregate, PredictionTicketId, PredictionTicketEvaluated> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        _evaluations.Add(new PredictionEvaluationSnapshot(
            e.EvaluatedAt, e.EvaluationRevision, e.HitTypeCodes,
            e.ScoreSummary, e.ReturnAmount, e.Roi));
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<PredictionTicketAggregate, PredictionTicketId, PredictionEvaluationRecalculated> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        _evaluations.Add(new PredictionEvaluationSnapshot(
            e.EvaluatedAt, e.EvaluationRevision, e.HitTypeCodes,
            e.ScoreSummary, e.ReturnAmount, e.Roi));
        EvaluationStatus = EvaluationStatus.RecalculationRequired;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<PredictionTicketAggregate, PredictionTicketId, PredictionMetadataCorrected> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        if (e.ConfidenceScore.HasValue) ConfidenceScore = e.ConfidenceScore.Value;
        if (e.SummaryComment != null) SummaryComment = e.SummaryComment;
        return Task.CompletedTask;
    }
}
