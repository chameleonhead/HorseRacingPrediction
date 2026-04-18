using EventFlow.Aggregates;
using EventFlow.ReadStores;
using HorseRacingPrediction.Domain.Races;

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
    private readonly List<EntryResultSnapshot> _entryResults = new();
    private readonly Dictionary<string, (string HorseId, int HorseNumber)> _entryIndex = new();

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
    public IReadOnlyList<EntryResultSnapshot> EntryResults => _entryResults.AsReadOnly();
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
        _entryIndex[e.EntryId] = (e.HorseId, e.HorseNumber);
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
