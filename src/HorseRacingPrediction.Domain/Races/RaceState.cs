using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public sealed class RaceState : AggregateState<RaceAggregate, RaceId, RaceState>,
    IApply<RaceCreated>,
    IApply<RaceCardPublished>,
    IApply<EntryRegistered>,
    IApply<EntryCollectedDataUpdated>,
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
    public TimeOnly? StartTime { get; private set; }
    public string? OverallPaceText { get; private set; }
    public string? CornerPassagesText { get; private set; }
    public string? CourseLayout { get; private set; }
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
        _entries.RemoveAll(x => x.EntryId == e.EntryId);
        _entries.Add(new EntryDetails(
            e.EntryId, e.HorseId, e.HorseNumber,
            e.JockeyId, e.TrainerId, e.GateNumber,
            e.AssignedWeight, e.SexCode, e.Age,
            e.DeclaredWeight, e.DeclaredWeightDiff,
            e.RunningStyleCode, e.OwnerName));
    }

    public void Apply(EntryCollectedDataUpdated e)
    {
        var index = _entries.FindIndex(x => x.EntryId == e.EntryId);
        if (index < 0) return;

        var current = _entries[index];
        _entries[index] = current with
        {
            DeclaredWeight = e.DeclaredWeight ?? current.DeclaredWeight,
            DeclaredWeightDiff = e.DeclaredWeightDiff ?? current.DeclaredWeightDiff,
            OwnerName = e.OwnerName ?? current.OwnerName,
        };
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
        if (Status < RaceStatus.ResultDeclared) Status = RaceStatus.ResultDeclared;
    }

    public void Apply(EntryResultDeclared e)
    {
        _entryResults.RemoveAll(x => x.EntryId == e.EntryId);
        _entryResults.Add(new EntryResultDetails(
            e.EntryId, e.FinishPosition, e.OfficialTime,
            e.MarginText, e.LastThreeFurlongTime,
            e.AbnormalResultCode, e.PrizeMoney, e.CornerPositions, e.Popularity, e.OriginalFinishPosition, e.IsDeadHeat, e.Average1F,
            e.AdditionalPrizeMoney));
    }

    public void Apply(PayoutResultDeclared e)
    {
        PayoutResult = new PayoutResultDetails(
            e.DeclaredAt, e.WinPayouts, e.PlacePayouts,
            e.QuinellaPayouts, e.ExactaPayouts, e.TrifectaPayouts, e.BracketQuinellaPayouts, e.WidePayouts, e.TrioPayouts);
        if (Status < RaceStatus.PayoutDeclared) Status = RaceStatus.PayoutDeclared;
    }

    public void Apply(RaceDataCorrected e)
    {
        if (e.EntryCount.HasValue) EntryCount = e.EntryCount;
        StartTime = e.StartTime ?? StartTime;
        OverallPaceText = e.OverallPaceText ?? OverallPaceText;
        CornerPassagesText = e.CornerPassagesText ?? CornerPassagesText;
        CourseLayout = e.CourseLayout ?? CourseLayout;
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
