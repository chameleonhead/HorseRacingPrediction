using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public partial class RaceAggregate : AggregateRoot<RaceAggregate, RaceId>,
    IEmit<RaceCreated>,
    IEmit<RaceCardPublished>,
    IEmit<EntryRegistered>,
    IEmit<EntryCollectedDataUpdated>,
    IEmit<RaceWeatherObserved>,
    IEmit<RaceTrackConditionObserved>,
    IEmit<RaceLifecycleStatusChanged>,
    IEmit<RaceStarted>,
    IEmit<RaceResultDeclared>,
    IEmit<EntryResultDeclared>,
    IEmit<PayoutResultDeclared>,
    IEmit<RaceDataCorrected>,
    IEmit<RaceOddsSnapshotRecorded>,
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
        decimal? declaredWeight = null, decimal? declaredWeightDiff = null,
        string? runningStyleCode = null, string? ownerName = null)
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Race is not created.");

        if (_state.Status == RaceStatus.Draft)
            throw new InvalidOperationException("Race card must be published before registering entries.");

        var previous = _state.Entries.LastOrDefault(x => x.EntryId == entryId);
        Emit(new EntryRegistered(entryId, horseId, horseNumber,
            jockeyId, trainerId, gateNumber, assignedWeight,
            sexCode, age, declaredWeight, declaredWeightDiff,
            runningStyleCode,
            _state.RaceDate, _state.RacecourseCode, _state.SurfaceCode,
            _state.DistanceMeters, _state.DirectionCode, _state.GradeCode,
            ownerName, previous?.HorseId, previous?.JockeyId));
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

    public void UpdateEntryCollectedData(
        string entryId,
        decimal? declaredWeight = null,
        decimal? declaredWeightDiff = null,
        string? ownerName = null)
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Race is not created.");

        var entry = _state.Entries.FirstOrDefault(x => x.EntryId == entryId)
            ?? throw new InvalidOperationException($"Entry '{entryId}' is not registered.");

        if (declaredWeight is null && declaredWeightDiff is null && ownerName is null)
            return;

        Emit(new EntryCollectedDataUpdated(
            entryId, entry.HorseId, declaredWeight, declaredWeightDiff, ownerName));
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
        string? abnormalResultCode = null, decimal? prizeMoney = null,
        string? cornerPositions = null, decimal? additionalPrizeMoney = null)
    {
        if (!_state.IsCreated)
            throw new InvalidOperationException("Race is not created.");

        if (_state.Status < RaceStatus.ResultDeclared)
            throw new InvalidOperationException("Entry result can only be declared after race result.");

        Emit(new EntryResultDeclared(entryId, finishPosition, officialTime,
            marginText, lastThreeFurlongTime, abnormalResultCode, prizeMoney, cornerPositions,
            additionalPrizeMoney: additionalPrizeMoney));
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

    public void RecordOddsSnapshot(DateTimeOffset observedAt, IReadOnlyList<RaceOddsEntry> entries,
        IReadOnlyList<RaceOddsObservation>? observations = null)
    {
        if (!_state.IsCreated) throw new InvalidOperationException("Race is not created.");
        observations ??= entries.Select(x => new RaceOddsObservation("Win", x.HorseNumber.ToString(),
            x.WinOdds, x.Popularity)).ToArray();
        if (observations.Count == 0 || observations.Any(x => string.IsNullOrWhiteSpace(x.Market)
            || string.IsNullOrWhiteSpace(x.Selection) || x.Value <= 0))
            throw new ArgumentException("Odds snapshot requires a market, selection, and positive value.", nameof(observations));
        if (entries.Any(x => x.HorseNumber <= 0 || x.WinOdds <= 0))
            throw new ArgumentException("Win odds require positive horse numbers and values.", nameof(entries));
        if (entries.Select(x => x.HorseNumber).Distinct().Count() != entries.Count)
            throw new ArgumentException("Horse numbers must be unique in an odds snapshot.", nameof(entries));
        if (observations.Select(x => $"{x.Market.Trim().ToUpperInvariant()}\u001f{x.Selection.Trim().ToUpperInvariant()}")
            .Distinct(StringComparer.Ordinal).Count() != observations.Count)
            throw new ArgumentException("Market and selection must be unique in an odds snapshot.", nameof(observations));
        Emit(new RaceOddsSnapshotRecorded(observedAt, entries, observations));
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
            _state.PayoutResult, _state.StartTime, _state.OverallPaceText, _state.CornerPassagesText, _state.CourseLayout);
    }

    public void Apply(RaceCreated e) { }
    public void Apply(RaceCardPublished e) { }
    public void Apply(EntryRegistered e) { }
    public void Apply(EntryCollectedDataUpdated e) { }
    public void Apply(RaceWeatherObserved e) { }
    public void Apply(RaceTrackConditionObserved e) { }
    public void Apply(RaceLifecycleStatusChanged e) { }
    public void Apply(RaceStarted e) { }
    public void Apply(RaceResultDeclared e) { }
    public void Apply(EntryResultDeclared e) { }
    public void Apply(PayoutResultDeclared e) { }
    public void Apply(RaceDataCorrected e) { }
    public void Apply(RaceOddsSnapshotRecorded e) { }
    public void Apply(RaceClosed e) { }
}
