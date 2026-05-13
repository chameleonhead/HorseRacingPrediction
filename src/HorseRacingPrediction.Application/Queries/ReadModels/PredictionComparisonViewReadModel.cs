using EventFlow.Aggregates;
using EventFlow.ReadStores;
using HorseRacingPrediction.Domain.Predictions;
using HorseRacingPrediction.Domain.Races;
using System.Text.Json.Serialization;

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
    public string RaceId { get; private set; } = string.Empty;
    public string? RaceName { get; private set; }
    public string? WinningHorseName { get; private set; }
    public DateTimeOffset? ResultDeclaredAt { get; private set; }
    [JsonIgnore]
    public List<PredictionComparisonTicketState> TicketStates { get; private set; } = [];
    [JsonIgnore]
    public List<RaceEntryIndexSnapshot> EntryIndexes { get; private set; } = [];
    public IReadOnlyList<PredictionTicketSnapshot> PredictionTickets =>
        TicketStates.Select(s => s.ToSnapshot()).ToList().AsReadOnly();
    public List<EntryResultSnapshot> EntryResults { get; private set; } = [];
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
        var index = EntryIndexes.FindIndex(x => x.EntryId == e.EntryId);
        var snapshot = new RaceEntryIndexSnapshot(e.EntryId, e.HorseId, e.HorseNumber);
        if (index >= 0)
            EntryIndexes[index] = snapshot;
        else
            EntryIndexes.Add(snapshot);
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
        var entryInfo = EntryIndexes.LastOrDefault(x => x.EntryId == e.EntryId);
        EntryResults.Add(new EntryResultSnapshot(
            e.EntryId,
            entryInfo?.HorseId ?? string.Empty,
            entryInfo?.HorseNumber ?? 0,
            e.FinishPosition, e.OfficialTime,
            e.MarginText, e.LastThreeFurlongTime,
            e.AbnormalResultCode, e.PrizeMoney, e.CornerPositions));
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
        var state = new PredictionComparisonTicketState
        {
            PredictionTicketId = ticketId,
            PredictorType = e.PredictorType,
            PredictorId = e.PredictorId,
            ConfidenceScore = e.ConfidenceScore,
            SummaryComment = e.SummaryComment,
            PredictedAt = e.PredictedAt
        };
        var index = TicketStates.FindIndex(x => x.PredictionTicketId == ticketId);
        if (index >= 0)
            TicketStates[index] = state;
        else
            TicketStates.Add(state);
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<PredictionTicketAggregate, PredictionTicketId, PredictionMarkAdded> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        var ticketId = domainEvent.AggregateIdentity.Value;
        var state = TicketStates.LastOrDefault(x => x.PredictionTicketId == ticketId);
        if (state is not null)
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
        var state = TicketStates.LastOrDefault(x => x.PredictionTicketId == ticketId);
        if (state is not null)
            state.Status = TicketStatus.Finalized;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<PredictionTicketAggregate, PredictionTicketId, PredictionTicketWithdrawn> domainEvent,
        CancellationToken cancellationToken)
    {
        var ticketId = domainEvent.AggregateIdentity.Value;
        var state = TicketStates.LastOrDefault(x => x.PredictionTicketId == ticketId);
        if (state is not null)
            state.Status = TicketStatus.Withdrawn;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<PredictionTicketAggregate, PredictionTicketId, PredictionTicketEvaluated> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        var ticketId = domainEvent.AggregateIdentity.Value;
        var state = TicketStates.LastOrDefault(x => x.PredictionTicketId == ticketId);
        if (state is not null)
        {
            state.Evaluations.Add(new PredictionEvaluationSnapshot(
                e.EvaluatedAt, e.EvaluationRevision, e.HitTypeCodes.ToList(),
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
        var state = TicketStates.LastOrDefault(x => x.PredictionTicketId == ticketId);
        if (state is not null)
        {
            state.Evaluations.Add(new PredictionEvaluationSnapshot(
                e.EvaluatedAt, e.EvaluationRevision, e.HitTypeCodes.ToList(),
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
        var state = TicketStates.LastOrDefault(x => x.PredictionTicketId == ticketId);
        if (state is not null)
        {
            if (e.ConfidenceScore.HasValue) state.ConfidenceScore = e.ConfidenceScore.Value;
            if (e.SummaryComment != null) state.SummaryComment = e.SummaryComment;
        }
        return Task.CompletedTask;
    }
}
