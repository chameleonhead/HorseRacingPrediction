using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public class RaceAggregate : AggregateRoot<RaceAggregate, RaceId>,
    IEmit<RaceCreated>,
    IEmit<RaceCardPublished>,
    IEmit<EntryRegistered>,
    IEmit<RaceWeatherObserved>,
    IEmit<RaceTrackConditionObserved>,
    IEmit<RaceLifecycleStatusChanged>,
    IEmit<RaceStarted>,
    IEmit<RaceResultDeclared>,
    IEmit<EntryResultDeclared>,
    IEmit<PayoutResultDeclared>,
    IEmit<RaceDataCorrected>,
    IEmit<RaceClosed>
{
    private readonly RaceState _state = new();

    public RaceAggregate(RaceId id)
        : base(id)
    {
        Register(_state);
    }

    public void Create(
        DateOnly raceDate,
        string racecourseCode,
        int raceNumber,
        string raceName,
        int? meetingNumber = null,
        int? dayNumber = null,
        string? gradeCode = null,
        string? surfaceCode = null,
        int? distanceMeters = null,
        string? directionCode = null)
    {
        if (_state.IsCreated)
            throw new InvalidOperationException("Race is already created.");

        Emit(new RaceCreated(raceDate, racecourseCode, raceNumber, raceName,
            meetingNumber, dayNumber, gradeCode, surfaceCode, distanceMeters, directionCode));
    }

    public void PublishCard(int entryCount)
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Race is not created.");

        if (_state.Status != RaceStatus.Draft)
            throw new InvalidOperationException("Race card can only be published from Draft state.");

        Emit(new RaceCardPublished(entryCount));
    }

    public void RegisterEntry(string entryId, string horseId, int horseNumber,
        string? jockeyId = null, string? trainerId = null,
        int? gateNumber = null, decimal? assignedWeight = null,
        string? sexCode = null, int? age = null,
        decimal? declaredWeight = null, decimal? declaredWeightDiff = null)
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Race is not created.");

        if (_state.Status == RaceStatus.Draft)
            throw new InvalidOperationException("Race card must be published before registering entries.");

        Emit(new EntryRegistered(entryId, horseId, horseNumber,
            jockeyId, trainerId, gateNumber, assignedWeight,
            sexCode, age, declaredWeight, declaredWeightDiff));
    }

    public void RecordWeatherObservation(DateTimeOffset observationTime,
        string? weatherCode = null, string? weatherText = null,
        decimal? temperatureCelsius = null, decimal? humidityPercent = null,
        string? windDirectionCode = null, decimal? windSpeedMeterPerSecond = null)
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Race is not created.");

        Emit(new RaceWeatherObserved(observationTime,
            weatherCode, weatherText, temperatureCelsius, humidityPercent,
            windDirectionCode, windSpeedMeterPerSecond));
    }

    public void RecordTrackConditionObservation(DateTimeOffset observationTime,
        string? turfConditionCode = null, string? dirtConditionCode = null,
        string? goingDescriptionText = null)
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Race is not created.");

        Emit(new RaceTrackConditionObserved(observationTime,
            turfConditionCode, dirtConditionCode, goingDescriptionText));
    }

    public void OpenPreRace()
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Race is not created.");

        if (_state.Status != RaceStatus.CardPublished)
            throw new InvalidOperationException("Pre-race can only be opened from CardPublished state.");

        Emit(new RaceLifecycleStatusChanged(RaceStatus.PreRaceOpen));
    }

    public void StartRace()
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Race is not created.");

        if (_state.Status != RaceStatus.PreRaceOpen)
            throw new InvalidOperationException("Race can only be started from PreRaceOpen state.");

        Emit(new RaceStarted());
    }

    public void DeclareResult(string winningHorseName, DateTimeOffset declaredAt,
        string? winningHorseId = null, string? stewardReportText = null)
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Race is not created.");

        if (_state.Status is not RaceStatus.CardPublished
            and not RaceStatus.PreRaceOpen
            and not RaceStatus.InProgress)
            throw new InvalidOperationException("Result can only be declared after card publication.");

        Emit(new RaceResultDeclared(winningHorseName, declaredAt, winningHorseId, stewardReportText));
    }

    public void DeclareEntryResult(string entryId,
        int? finishPosition = null, string? officialTime = null,
        string? marginText = null, string? lastThreeFurlongTime = null,
        string? abnormalResultCode = null, decimal? prizeMoney = null)
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Race is not created.");

        if (_state.Status < RaceStatus.ResultDeclared)
            throw new InvalidOperationException("Entry result can only be declared after race result.");

        Emit(new EntryResultDeclared(entryId, finishPosition, officialTime,
            marginText, lastThreeFurlongTime, abnormalResultCode, prizeMoney));
    }

    public void DeclarePayoutResult(DateTimeOffset declaredAt,
        IReadOnlyList<PayoutEntry>? winPayouts = null,
        IReadOnlyList<PayoutEntry>? placePayouts = null,
        IReadOnlyList<PayoutEntry>? quinellaPayouts = null,
        IReadOnlyList<PayoutEntry>? exactaPayouts = null,
        IReadOnlyList<PayoutEntry>? trifectaPayouts = null)
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Race is not created.");

        if (_state.Status != RaceStatus.ResultDeclared)
            throw new InvalidOperationException("Payout can only be declared from ResultDeclared state.");

        Emit(new PayoutResultDeclared(declaredAt, winPayouts, placePayouts,
            quinellaPayouts, exactaPayouts, trifectaPayouts));
    }

    public void CloseRaceLifecycle()
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Race is not created.");

        if (_state.Status is not RaceStatus.PayoutDeclared and not RaceStatus.ResultDeclared)
            throw new InvalidOperationException("Race can only be closed from ResultDeclared or PayoutDeclared state.");

        Emit(new RaceClosed());
    }

    public void CorrectRaceData(string? raceName = null, string? racecourseCode = null,
        int? raceNumber = null, string? gradeCode = null,
        string? surfaceCode = null, int? distanceMeters = null,
        string? directionCode = null, string? reason = null)
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Race is not created.");

        Emit(new RaceDataCorrected(raceName, racecourseCode, raceNumber,
            gradeCode, surfaceCode, distanceMeters, directionCode, reason));
    }

    public RaceDetails GetDetails()
    {
        return new RaceDetails(
            Id.Value,
            _state.RaceDate,
            _state.RacecourseCode,
            _state.RaceNumber,
            _state.RaceName,
            _state.Status,
            _state.MeetingNumber,
            _state.DayNumber,
            _state.GradeCode,
            _state.SurfaceCode,
            _state.DistanceMeters,
            _state.DirectionCode,
            _state.EntryCount,
            _state.Entries,
            _state.WeatherObservations,
            _state.TrackConditionObservations,
            _state.WinningHorseName,
            _state.WinningHorseId,
            _state.StewardReportText,
            _state.ResultDeclaredAt,
            _state.EntryResults,
            _state.PayoutResult);
    }

    public void Apply(RaceCreated e) { }
    public void Apply(RaceCardPublished e) { }
    public void Apply(EntryRegistered e) { }
    public void Apply(RaceWeatherObserved e) { }
    public void Apply(RaceTrackConditionObserved e) { }
    public void Apply(RaceLifecycleStatusChanged e) { }
    public void Apply(RaceStarted e) { }
    public void Apply(RaceResultDeclared e) { }
    public void Apply(EntryResultDeclared e) { }
    public void Apply(PayoutResultDeclared e) { }
    public void Apply(RaceDataCorrected e) { }
    public void Apply(RaceClosed e) { }
}
