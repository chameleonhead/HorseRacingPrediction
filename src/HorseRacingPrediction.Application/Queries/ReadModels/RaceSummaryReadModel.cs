using EventFlow.Aggregates;
using EventFlow.ReadStores;
using HorseRacingPrediction.Domain.Races;
using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public class RaceSummaryReadModel : IReadModel,
    IAmReadModelFor<RaceAggregate, RaceId, RaceCreated>,
    IAmReadModelFor<RaceAggregate, RaceId, RaceCardPublished>,
    IAmReadModelFor<RaceAggregate, RaceId, EntryRegistered>,
    IAmReadModelFor<RaceAggregate, RaceId, RaceLifecycleStatusChanged>,
    IAmReadModelFor<RaceAggregate, RaceId, RaceStarted>,
    IAmReadModelFor<RaceAggregate, RaceId, RaceResultDeclared>,
    IAmReadModelFor<RaceAggregate, RaceId, PayoutResultDeclared>,
    IAmReadModelFor<RaceAggregate, RaceId, RaceDataCorrected>,
    IAmReadModelFor<RaceAggregate, RaceId, RaceClosed>
{
    [Key]
    public string RaceId { get; private set; } = string.Empty;
    public DateOnly? RaceDate { get; private set; }
    public string? RacecourseCode { get; private set; }
    public int? RaceNumber { get; private set; }
    public string? RaceName { get; private set; }
    public RaceStatus Status { get; private set; } = RaceStatus.Draft;
    public int? EntryCount { get; private set; }
    public string? WinningHorseName { get; private set; }
    public DateTimeOffset? ResultDeclaredAt { get; private set; }

    public Task ApplyAsync(
        IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceCreated> domainEvent,
        CancellationToken cancellationToken)
    {
        var aggregateEvent = domainEvent.AggregateEvent;
        RaceId = domainEvent.AggregateIdentity.Value;
        RaceDate = aggregateEvent.RaceDate;
        RacecourseCode = aggregateEvent.RacecourseCode;
        RaceNumber = aggregateEvent.RaceNumber;
        RaceName = aggregateEvent.RaceName;
        Status = RaceStatus.Draft;
        EntryCount = 0;
        WinningHorseName = null;
        ResultDeclaredAt = null;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(
        IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceCardPublished> domainEvent,
        CancellationToken cancellationToken)
    {
        EntryCount = domainEvent.AggregateEvent.EntryCount;
        Status = RaceStatus.CardPublished;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(
        IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, EntryRegistered> domainEvent,
        CancellationToken cancellationToken)
    {
        EntryCount = (EntryCount ?? 0) + 1;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(
        IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceLifecycleStatusChanged> domainEvent,
        CancellationToken cancellationToken)
    {
        Status = domainEvent.AggregateEvent.NewStatus;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(
        IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceStarted> domainEvent,
        CancellationToken cancellationToken)
    {
        Status = RaceStatus.InProgress;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(
        IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceResultDeclared> domainEvent,
        CancellationToken cancellationToken)
    {
        var aggregateEvent = domainEvent.AggregateEvent;
        WinningHorseName = aggregateEvent.WinningHorseName;
        ResultDeclaredAt = aggregateEvent.DeclaredAt;
        Status = RaceStatus.ResultDeclared;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(
        IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, PayoutResultDeclared> domainEvent,
        CancellationToken cancellationToken)
    {
        Status = RaceStatus.PayoutDeclared;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(
        IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceDataCorrected> domainEvent,
        CancellationToken cancellationToken)
    {
        var aggregateEvent = domainEvent.AggregateEvent;
        if (aggregateEvent.RaceName != null)
            RaceName = aggregateEvent.RaceName;

        if (aggregateEvent.RacecourseCode != null)
            RacecourseCode = aggregateEvent.RacecourseCode;

        if (aggregateEvent.RaceNumber.HasValue)
            RaceNumber = aggregateEvent.RaceNumber;

        return Task.CompletedTask;
    }

    public Task ApplyAsync(
        IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceClosed> domainEvent,
        CancellationToken cancellationToken)
    {
        Status = RaceStatus.Closed;
        return Task.CompletedTask;
    }
}