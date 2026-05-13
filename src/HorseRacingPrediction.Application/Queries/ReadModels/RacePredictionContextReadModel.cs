using EventFlow.Aggregates;
using EventFlow.ReadStores;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public class RacePredictionContextReadModel : IReadModel,
    IAmReadModelFor<RaceAggregate, RaceId, RaceCreated>,
    IAmReadModelFor<RaceAggregate, RaceId, RaceCardPublished>,
    IAmReadModelFor<RaceAggregate, RaceId, EntryRegistered>,
    IAmReadModelFor<RaceAggregate, RaceId, RaceWeatherObserved>,
    IAmReadModelFor<RaceAggregate, RaceId, RaceTrackConditionObserved>,
    IAmReadModelFor<RaceAggregate, RaceId, RaceLifecycleStatusChanged>,
    IAmReadModelFor<RaceAggregate, RaceId, RaceStarted>,
    IAmReadModelFor<RaceAggregate, RaceId, RaceDataCorrected>,
    IAmReadModelFor<RaceAggregate, RaceId, RaceClosed>
{
    public string RaceId { get; private set; } = string.Empty;
    public DateOnly? RaceDate { get; private set; }
    public string? RacecourseCode { get; private set; }
    public int? RaceNumber { get; private set; }
    public string? RaceName { get; private set; }
    public RaceStatus Status { get; private set; } = RaceStatus.Draft;
    public string? GradeCode { get; private set; }
    public string? SurfaceCode { get; private set; }
    public int? DistanceMeters { get; private set; }
    public string? DirectionCode { get; private set; }
    public List<RacePredictionContextEntry> Entries { get; private set; } = [];
    public List<WeatherObservationSnapshot> WeatherObservations { get; private set; } = [];
    public WeatherObservationSnapshot? LatestWeather => WeatherObservations.LastOrDefault();
    public List<TrackConditionSnapshot> TrackConditionObservations { get; private set; } = [];
    public TrackConditionSnapshot? LatestTrackCondition => TrackConditionObservations.LastOrDefault();

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
        GradeCode = e.GradeCode;
        SurfaceCode = e.SurfaceCode;
        DistanceMeters = e.DistanceMeters;
        DirectionCode = e.DirectionCode;
        Status = RaceStatus.Draft;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceCardPublished> domainEvent,
        CancellationToken cancellationToken)
    {
        Status = RaceStatus.CardPublished;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, EntryRegistered> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        Entries.Add(new RacePredictionContextEntry(
            e.EntryId, e.HorseId, e.HorseNumber,
            e.JockeyId, e.TrainerId, e.GateNumber, e.AssignedWeight,
            e.SexCode, e.Age, e.DeclaredWeight, e.DeclaredWeightDiff,
            e.RunningStyleCode));
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceWeatherObserved> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        WeatherObservations.Add(new WeatherObservationSnapshot(
            e.ObservationTime, e.WeatherCode, e.WeatherText,
            e.TemperatureCelsius, e.HumidityPercent,
            e.WindDirectionCode, e.WindSpeedMeterPerSecond));
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, RaceTrackConditionObserved> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        TrackConditionObservations.Add(new TrackConditionSnapshot(
            e.ObservationTime, e.TurfConditionCode, e.DirtConditionCode, e.GoingDescriptionText));
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
        IDomainEvent<RaceAggregate, RaceId, RaceDataCorrected> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        if (e.RaceName != null) RaceName = e.RaceName;
        if (e.RacecourseCode != null) RacecourseCode = e.RacecourseCode;
        if (e.RaceNumber.HasValue) RaceNumber = e.RaceNumber;
        if (e.GradeCode != null) GradeCode = e.GradeCode;
        if (e.SurfaceCode != null) SurfaceCode = e.SurfaceCode;
        if (e.DistanceMeters.HasValue) DistanceMeters = e.DistanceMeters;
        if (e.DirectionCode != null) DirectionCode = e.DirectionCode;
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
