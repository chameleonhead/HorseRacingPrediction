using EventFlow.Aggregates;
using EventFlow.ReadStores;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public partial class RacePredictionContextReadModel : IAmReadModelFor<RaceAggregate, RaceId, RaceEntryAssignmentsRepaired>
{
    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceEntryAssignmentsRepaired> domainEvent, CancellationToken token)
    {
        Entries = domainEvent.AggregateEvent.Entries.Select(e => new RacePredictionContextEntry(
            e.EntryId, e.HorseId, e.HorseNumber, e.JockeyId, e.TrainerId, e.GateNumber,
            e.AssignedWeight, e.SexCode, e.Age, e.DeclaredWeight, e.DeclaredWeightDiff,
            e.RunningStyleCode, e.OwnerName)).ToList();
        GradeCode = domainEvent.AggregateEvent.GradeCode;
        return Task.CompletedTask;
    }
}

public partial class RaceResultViewReadModel : IAmReadModelFor<RaceAggregate, RaceId, RaceEntryAssignmentsRepaired>
{
    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceEntryAssignmentsRepaired> domainEvent, CancellationToken token)
    {
        EntryIndexes = domainEvent.AggregateEvent.Entries.Select(e =>
            new RaceEntryIndexSnapshot(e.EntryId, e.HorseId, e.HorseNumber, e.GateNumber)).ToList();
        EntryCount = EntryIndexes.Count;
        return Task.CompletedTask;
    }
}

public partial class PredictionComparisonViewReadModel : IAmReadModelFor<RaceAggregate, RaceId, RaceEntryAssignmentsRepaired>
{
    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceEntryAssignmentsRepaired> domainEvent, CancellationToken token)
    {
        EntryIndexes = domainEvent.AggregateEvent.Entries.Select(e =>
            new RaceEntryIndexSnapshot(e.EntryId, e.HorseId, e.HorseNumber, e.GateNumber)).ToList();
        return Task.CompletedTask;
    }
}

public partial class RaceSummaryReadModel : IAmReadModelFor<RaceAggregate, RaceId, RaceEntryAssignmentsRepaired>
{
    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceEntryAssignmentsRepaired> domainEvent, CancellationToken token)
    {
        EntryCount = domainEvent.AggregateEvent.Entries.Count;
        return Task.CompletedTask;
    }
}

public partial class HorseRaceHistoryReadModel : IAmReadModelFor<RaceAggregate, RaceId, RaceEntryAssignmentsRepaired>
{
    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceEntryAssignmentsRepaired> domainEvent, CancellationToken token)
    {
        var repair = domainEvent.AggregateEvent;
        var raceId = domainEvent.AggregateIdentity.Value;
        Entries.RemoveAll(x => x.RaceId == raceId);
        foreach (var e in repair.Entries.Where(x => x.HorseId == HorseId))
            Entries.Add(new HorseRaceHistoryEntry(raceId, e.EntryId, repair.RaceDate, repair.RacecourseCode,
                repair.SurfaceCode, repair.DistanceMeters, repair.DirectionCode, repair.GradeCode,
                e.GateNumber, e.AssignedWeight, e.DeclaredWeight, e.DeclaredWeightDiff,
                e.RunningStyleCode, e.JockeyId, e.TrainerId, null, null, null, null));
        return Task.CompletedTask;
    }
}

public partial class JockeyRaceHistoryReadModel : IAmReadModelFor<RaceAggregate, RaceId, RaceEntryAssignmentsRepaired>
{
    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceEntryAssignmentsRepaired> domainEvent, CancellationToken token)
    {
        var repair = domainEvent.AggregateEvent;
        var raceId = domainEvent.AggregateIdentity.Value;
        Entries.RemoveAll(x => x.RaceId == raceId);
        foreach (var e in repair.Entries.Where(x => x.JockeyId == JockeyId))
            Entries.Add(new JockeyRaceHistoryEntry(raceId, e.EntryId, e.HorseId,
                repair.RaceDate, repair.RacecourseCode, repair.SurfaceCode, repair.DistanceMeters,
                repair.DirectionCode, repair.GradeCode, null, null));
        return Task.CompletedTask;
    }
}

public partial class HorseWeightHistoryReadModel : IAmReadModelFor<RaceAggregate, RaceId, RaceEntryAssignmentsRepaired>
{
    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceEntryAssignmentsRepaired> domainEvent, CancellationToken token)
    {
        var raceId = domainEvent.AggregateIdentity.Value;
        WeightHistory.RemoveAll(x => x.RaceId == raceId);
        foreach (var e in domainEvent.AggregateEvent.Entries.Where(x => x.HorseId == HorseId))
            WeightHistory.Add(new HorseWeightEntry(raceId, e.EntryId, domainEvent.AggregateEvent.ObservedAt,
                e.DeclaredWeight, e.DeclaredWeightDiff));
        return Task.CompletedTask;
    }
}
