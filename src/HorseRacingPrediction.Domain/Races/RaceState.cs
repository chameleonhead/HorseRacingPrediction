using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public sealed class RaceState : AggregateState<RaceAggregate, RaceId, RaceState>,
    IApply<RaceCreated>,
    IApply<RaceCardPublished>,
    IApply<EntryRegistered>,
    IApply<RaceWeatherObserved>,
    IApply<RaceTrackConditionObserved>,
    IApply<RaceLifecycleStatusChanged>,
    IApply<RaceStarted>,
    IApply<RaceResultDeclared>,
    IApply<EntryResultDeclared>,
    IApply<PayoutResultDeclared>,
    IApply<RaceDataCorrected>,
    IApply<RaceClosed>
{
    private readonly List<EntryDetails> _entries = new();
    private readonly List<WeatherObservationDetails> _weatherObservations = new();
    private readonly List<TrackConditionObservationDetails> _trackConditionObservations = new();
    private readonly List<EntryResultDetails> _entryResults = new();

    public bool IsCreated { get; private set; }
    public DateOnly? RaceDate { get; private set; }
    public string? RacecourseCode { get; private set; }
    public int? RaceNumber { get; private set; }
    public string? RaceName { get; private set; }
    public RaceStatus Status { get; private set; } = RaceStatus.Draft;
    public int? MeetingNumber { get; private set; }
    public int? DayNumber { get; private set; }
    public string? GradeCode { get; private set; }
    public string? SurfaceCode { get; private set; }
    public int? DistanceMeters { get; private set; }
    public string? DirectionCode { get; private set; }
    public int? EntryCount { get; private set; }
    public IReadOnlyList<EntryDetails> Entries => _entries.AsReadOnly();
    public IReadOnlyList<WeatherObservationDetails> WeatherObservations => _weatherObservations.AsReadOnly();
    public IReadOnlyList<TrackConditionObservationDetails> TrackConditionObservations => _trackConditionObservations.AsReadOnly();
    public string? WinningHorseName { get; private set; }
    public string? WinningHorseId { get; private set; }
    public string? StewardReportText { get; private set; }
    public DateTimeOffset? ResultDeclaredAt { get; private set; }
    public IReadOnlyList<EntryResultDetails> EntryResults => _entryResults.AsReadOnly();
    public PayoutResultDetails? PayoutResult { get; private set; }

    public void Apply(RaceCreated e)
    {
        IsCreated = true;
        RaceDate = e.RaceDate;
        RacecourseCode = e.RacecourseCode;
        RaceNumber = e.RaceNumber;
        RaceName = e.RaceName;
        MeetingNumber = e.MeetingNumber;
        DayNumber = e.DayNumber;
        GradeCode = e.GradeCode;
        SurfaceCode = e.SurfaceCode;
        DistanceMeters = e.DistanceMeters;
        DirectionCode = e.DirectionCode;
        Status = RaceStatus.Draft;
    }

    public void Apply(RaceCardPublished e)
    {
        EntryCount = e.EntryCount;
        Status = RaceStatus.CardPublished;
    }

    public void Apply(EntryRegistered e)
    {
        _entries.Add(new EntryDetails(
            e.EntryId, e.HorseId, e.HorseNumber,
            e.JockeyId, e.TrainerId, e.GateNumber,
            e.AssignedWeight, e.SexCode, e.Age,
            e.DeclaredWeight, e.DeclaredWeightDiff));
    }

    public void Apply(RaceWeatherObserved e)
    {
        _weatherObservations.Add(new WeatherObservationDetails(
            e.ObservationTime, e.WeatherCode, e.WeatherText,
            e.TemperatureCelsius, e.HumidityPercent,
            e.WindDirectionCode, e.WindSpeedMeterPerSecond));
    }

    public void Apply(RaceTrackConditionObserved e)
    {
        _trackConditionObservations.Add(new TrackConditionObservationDetails(
            e.ObservationTime, e.TurfConditionCode, e.DirtConditionCode,
            e.GoingDescriptionText));
    }

    public void Apply(RaceLifecycleStatusChanged e)
    {
        Status = e.NewStatus;
    }

    public void Apply(RaceStarted e)
    {
        Status = RaceStatus.InProgress;
    }

    public void Apply(RaceResultDeclared e)
    {
        WinningHorseName = e.WinningHorseName;
        WinningHorseId = e.WinningHorseId;
        StewardReportText = e.StewardReportText;
        ResultDeclaredAt = e.DeclaredAt;
        Status = RaceStatus.ResultDeclared;
    }

    public void Apply(EntryResultDeclared e)
    {
        _entryResults.Add(new EntryResultDetails(
            e.EntryId, e.FinishPosition, e.OfficialTime,
            e.MarginText, e.LastThreeFurlongTime,
            e.AbnormalResultCode, e.PrizeMoney));
    }

    public void Apply(PayoutResultDeclared e)
    {
        PayoutResult = new PayoutResultDetails(
            e.DeclaredAt, e.WinPayouts, e.PlacePayouts,
            e.QuinellaPayouts, e.ExactaPayouts, e.TrifectaPayouts);
        Status = RaceStatus.PayoutDeclared;
    }

    public void Apply(RaceDataCorrected e)
    {
        if (e.RaceName != null) RaceName = e.RaceName;
        if (e.RacecourseCode != null) RacecourseCode = e.RacecourseCode;
        if (e.RaceNumber.HasValue) RaceNumber = e.RaceNumber;
        if (e.GradeCode != null) GradeCode = e.GradeCode;
        if (e.SurfaceCode != null) SurfaceCode = e.SurfaceCode;
        if (e.DistanceMeters.HasValue) DistanceMeters = e.DistanceMeters;
        if (e.DirectionCode != null) DirectionCode = e.DirectionCode;
    }

    public void Apply(RaceClosed e)
    {
        Status = RaceStatus.Closed;
    }
}
