using EventFlow.Aggregates;
using EventFlow.ReadStores;
using HorseRacingPrediction.Domain.Races;
using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public class RaceResultViewReadModel : IReadModel,
    IAmReadModelFor<RaceAggregate, RaceId, RaceCreated>,
    IAmReadModelFor<RaceAggregate, RaceId, RaceCardPublished>,
    IAmReadModelFor<RaceAggregate, RaceId, EntryRegistered>,
    IAmReadModelFor<RaceAggregate, RaceId, RaceLifecycleStatusChanged>,
    IAmReadModelFor<RaceAggregate, RaceId, RaceStarted>,
    IAmReadModelFor<RaceAggregate, RaceId, RaceResultDeclared>,
    IAmReadModelFor<RaceAggregate, RaceId, EntryResultDeclared>,
    IAmReadModelFor<RaceAggregate, RaceId, PayoutResultDeclared>,
    IAmReadModelFor<RaceAggregate, RaceId, RaceDataCorrected>,
    IAmReadModelFor<RaceAggregate, RaceId, RaceClosed>
{
    public string RaceId { get; private set; } = string.Empty;
    public DateOnly? RaceDate { get; private set; }
    public string? RacecourseCode { get; private set; }
    public int? RaceNumber { get; private set; }
    public string? RaceName { get; private set; }
    public RaceStatus Status { get; private set; } = RaceStatus.Draft;
    public int? EntryCount { get; private set; }
    public string? WinningHorseName { get; private set; }
    public string? WinningHorseId { get; private set; }
    public DateTimeOffset? ResultDeclaredAt { get; private set; }
    public string? StewardReportText { get; private set; }
    public List<EntryResultSnapshot> EntryResults { get; private set; } = [];
    [JsonIgnore]
    public List<RaceEntryIndexSnapshot> EntryIndexes { get; private set; } = [];
    public PayoutResultSnapshot? PayoutResult { get; private set; }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceCreated> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        RaceId = domainEvent.AggregateIdentity.Value;
        RaceDate = e.RaceDate;
        RacecourseCode = e.RacecourseCode;
        RaceNumber = e.RaceNumber;
        RaceName = e.RaceName;
        Status = RaceStatus.Draft;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceCardPublished> domainEvent,
        CancellationToken cancellationToken)
    {
        EntryCount = domainEvent.AggregateEvent.EntryCount;
        Status = RaceStatus.CardPublished;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, EntryRegistered> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        var index = EntryIndexes.FindIndex(x => x.EntryId == e.EntryId);
        var snapshot = new RaceEntryIndexSnapshot(e.EntryId, e.HorseId, e.HorseNumber, e.GateNumber);
        if (index >= 0)
            EntryIndexes[index] = snapshot;
        else
            EntryIndexes.Add(snapshot);
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceLifecycleStatusChanged> domainEvent,
        CancellationToken cancellationToken)
    {
        Status = domainEvent.AggregateEvent.NewStatus;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceStarted> domainEvent,
        CancellationToken cancellationToken)
    {
        Status = RaceStatus.InProgress;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceResultDeclared> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        WinningHorseName = e.WinningHorseName;
        WinningHorseId = e.WinningHorseId;
        ResultDeclaredAt = e.DeclaredAt;
        StewardReportText = e.StewardReportText;
        Status = RaceStatus.ResultDeclared;
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
        Status = RaceStatus.PayoutDeclared;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceDataCorrected> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        if (e.RaceName != null) RaceName = e.RaceName;
        if (e.RacecourseCode != null) RacecourseCode = e.RacecourseCode;
        if (e.RaceNumber.HasValue) RaceNumber = e.RaceNumber;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceClosed> domainEvent,
        CancellationToken cancellationToken)
    {
        Status = RaceStatus.Closed;
        return Task.CompletedTask;
    }
}
