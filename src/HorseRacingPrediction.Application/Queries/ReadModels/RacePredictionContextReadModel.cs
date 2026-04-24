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
    private readonly List<RacePredictionContextEntry> _entries = new();
    private readonly List<WeatherObservationSnapshot> _weatherObservations = new();
    private readonly List<TrackConditionSnapshot> _trackConditionObservations = new();

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
    public IReadOnlyList<RacePredictionContextEntry> Entries => _entries.AsReadOnly();
    public IReadOnlyList<WeatherObservationSnapshot> WeatherObservations => _weatherObservations.AsReadOnly();
    public WeatherObservationSnapshot? LatestWeather => _weatherObservations.LastOrDefault();
    public IReadOnlyList<TrackConditionSnapshot> TrackConditionObservations => _trackConditionObservations.AsReadOnly();
    public TrackConditionSnapshot? LatestTrackCondition => _trackConditionObservations.LastOrDefault();

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
        _entries.Add(new RacePredictionContextEntry(
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
        _weatherObservations.Add(new WeatherObservationSnapshot(
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
        _trackConditionObservations.Add(new TrackConditionSnapshot(
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
