using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public sealed class RaceCreated : AggregateEvent<RaceAggregate, RaceId>
{
    public RaceCreated(DateOnly raceDate, string racecourseCode, int raceNumber, string raceName,
        int? meetingNumber = null, int? dayNumber = null, string? gradeCode = null,
        string? surfaceCode = null, int? distanceMeters = null, string? directionCode = null)
    {
        RaceDate = raceDate;
        RacecourseCode = racecourseCode;
        RaceNumber = raceNumber;
        RaceName = raceName;
        MeetingNumber = meetingNumber;
        DayNumber = dayNumber;
        GradeCode = gradeCode;
        SurfaceCode = surfaceCode;
        DistanceMeters = distanceMeters;
        DirectionCode = directionCode;
    }

    public DateOnly RaceDate { get; }
    public string RacecourseCode { get; }
    public int RaceNumber { get; }
    public string RaceName { get; }
    public int? MeetingNumber { get; }
    public int? DayNumber { get; }
    public string? GradeCode { get; }
    public string? SurfaceCode { get; }
    public int? DistanceMeters { get; }
    public string? DirectionCode { get; }
}

public sealed class RaceCardPublished : AggregateEvent<RaceAggregate, RaceId>
{
    public RaceCardPublished(int entryCount)
    {
        EntryCount = entryCount;
    }

    public int EntryCount { get; }
}

public sealed class EntryRegistered : AggregateEvent<RaceAggregate, RaceId>
{
    public EntryRegistered(string entryId, string horseId, int horseNumber,
        string? jockeyId = null, string? trainerId = null,
        int? gateNumber = null, decimal? assignedWeight = null,
        string? sexCode = null, int? age = null,
        decimal? declaredWeight = null, decimal? declaredWeightDiff = null)
    {
        EntryId = entryId;
        HorseId = horseId;
        HorseNumber = horseNumber;
        JockeyId = jockeyId;
        TrainerId = trainerId;
        GateNumber = gateNumber;
        AssignedWeight = assignedWeight;
        SexCode = sexCode;
        Age = age;
        DeclaredWeight = declaredWeight;
        DeclaredWeightDiff = declaredWeightDiff;
    }

    public string EntryId { get; }
    public string HorseId { get; }
    public int HorseNumber { get; }
    public string? JockeyId { get; }
    public string? TrainerId { get; }
    public int? GateNumber { get; }
    public decimal? AssignedWeight { get; }
    public string? SexCode { get; }
    public int? Age { get; }
    public decimal? DeclaredWeight { get; }
    public decimal? DeclaredWeightDiff { get; }
}

public sealed class RaceWeatherObserved : AggregateEvent<RaceAggregate, RaceId>
{
    public RaceWeatherObserved(DateTimeOffset observationTime,
        string? weatherCode = null, string? weatherText = null,
        decimal? temperatureCelsius = null, decimal? humidityPercent = null,
        string? windDirectionCode = null, decimal? windSpeedMeterPerSecond = null)
    {
        ObservationTime = observationTime;
        WeatherCode = weatherCode;
        WeatherText = weatherText;
        TemperatureCelsius = temperatureCelsius;
        HumidityPercent = humidityPercent;
        WindDirectionCode = windDirectionCode;
        WindSpeedMeterPerSecond = windSpeedMeterPerSecond;
    }

    public DateTimeOffset ObservationTime { get; }
    public string? WeatherCode { get; }
    public string? WeatherText { get; }
    public decimal? TemperatureCelsius { get; }
    public decimal? HumidityPercent { get; }
    public string? WindDirectionCode { get; }
    public decimal? WindSpeedMeterPerSecond { get; }
}

public sealed class RaceTrackConditionObserved : AggregateEvent<RaceAggregate, RaceId>
{
    public RaceTrackConditionObserved(DateTimeOffset observationTime,
        string? turfConditionCode = null, string? dirtConditionCode = null,
        string? goingDescriptionText = null)
    {
        ObservationTime = observationTime;
        TurfConditionCode = turfConditionCode;
        DirtConditionCode = dirtConditionCode;
        GoingDescriptionText = goingDescriptionText;
    }

    public DateTimeOffset ObservationTime { get; }
    public string? TurfConditionCode { get; }
    public string? DirtConditionCode { get; }
    public string? GoingDescriptionText { get; }
}

public sealed class RaceLifecycleStatusChanged : AggregateEvent<RaceAggregate, RaceId>
{
    public RaceLifecycleStatusChanged(RaceStatus newStatus)
    {
        NewStatus = newStatus;
    }

    public RaceStatus NewStatus { get; }
}

public sealed class RaceStarted : AggregateEvent<RaceAggregate, RaceId>
{
}

public sealed class RaceResultDeclared : AggregateEvent<RaceAggregate, RaceId>
{
    public RaceResultDeclared(string winningHorseName, DateTimeOffset declaredAt,
        string? winningHorseId = null, string? stewardReportText = null)
    {
        WinningHorseName = winningHorseName;
        DeclaredAt = declaredAt;
        WinningHorseId = winningHorseId;
        StewardReportText = stewardReportText;
    }

    public string WinningHorseName { get; }
    public DateTimeOffset DeclaredAt { get; }
    public string? WinningHorseId { get; }
    public string? StewardReportText { get; }
}

public sealed class EntryResultDeclared : AggregateEvent<RaceAggregate, RaceId>
{
    public EntryResultDeclared(string entryId,
        int? finishPosition = null, string? officialTime = null,
        string? marginText = null, string? lastThreeFurlongTime = null,
        string? abnormalResultCode = null, decimal? prizeMoney = null)
    {
        EntryId = entryId;
        FinishPosition = finishPosition;
        OfficialTime = officialTime;
        MarginText = marginText;
        LastThreeFurlongTime = lastThreeFurlongTime;
        AbnormalResultCode = abnormalResultCode;
        PrizeMoney = prizeMoney;
    }

    public string EntryId { get; }
    public int? FinishPosition { get; }
    public string? OfficialTime { get; }
    public string? MarginText { get; }
    public string? LastThreeFurlongTime { get; }
    public string? AbnormalResultCode { get; }
    public decimal? PrizeMoney { get; }
}

public sealed class PayoutResultDeclared : AggregateEvent<RaceAggregate, RaceId>
{
    public PayoutResultDeclared(DateTimeOffset declaredAt,
        IReadOnlyList<PayoutEntry>? winPayouts = null,
        IReadOnlyList<PayoutEntry>? placePayouts = null,
        IReadOnlyList<PayoutEntry>? quinellaPayouts = null,
        IReadOnlyList<PayoutEntry>? exactaPayouts = null,
        IReadOnlyList<PayoutEntry>? trifectaPayouts = null)
    {
        DeclaredAt = declaredAt;
        WinPayouts = winPayouts ?? Array.Empty<PayoutEntry>();
        PlacePayouts = placePayouts ?? Array.Empty<PayoutEntry>();
        QuinellaPayouts = quinellaPayouts ?? Array.Empty<PayoutEntry>();
        ExactaPayouts = exactaPayouts ?? Array.Empty<PayoutEntry>();
        TrifectaPayouts = trifectaPayouts ?? Array.Empty<PayoutEntry>();
    }

    public DateTimeOffset DeclaredAt { get; }
    public IReadOnlyList<PayoutEntry> WinPayouts { get; }
    public IReadOnlyList<PayoutEntry> PlacePayouts { get; }
    public IReadOnlyList<PayoutEntry> QuinellaPayouts { get; }
    public IReadOnlyList<PayoutEntry> ExactaPayouts { get; }
    public IReadOnlyList<PayoutEntry> TrifectaPayouts { get; }
}

public sealed class RaceDataCorrected : AggregateEvent<RaceAggregate, RaceId>
{
    public RaceDataCorrected(string? raceName = null, string? racecourseCode = null,
        int? raceNumber = null, string? gradeCode = null,
        string? surfaceCode = null, int? distanceMeters = null,
        string? directionCode = null, string? reason = null)
    {
        RaceName = raceName;
        RacecourseCode = racecourseCode;
        RaceNumber = raceNumber;
        GradeCode = gradeCode;
        SurfaceCode = surfaceCode;
        DistanceMeters = distanceMeters;
        DirectionCode = directionCode;
        Reason = reason;
    }

    public string? RaceName { get; }
    public string? RacecourseCode { get; }
    public int? RaceNumber { get; }
    public string? GradeCode { get; }
    public string? SurfaceCode { get; }
    public int? DistanceMeters { get; }
    public string? DirectionCode { get; }
    public string? Reason { get; }
}

public sealed class RaceClosed : AggregateEvent<RaceAggregate, RaceId>
{
}
