using EventFlow.Commands;

namespace HorseRacingPrediction.Domain.Races;

public sealed class CreateRaceCommand : Command<RaceAggregate, RaceId>
{
    public CreateRaceCommand(RaceId aggregateId, DateOnly raceDate, string racecourseCode, int raceNumber, string raceName,
        int? meetingNumber = null, int? dayNumber = null, string? gradeCode = null,
        string? surfaceCode = null, int? distanceMeters = null, string? directionCode = null)
        : base(aggregateId)
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

public sealed class CreateRaceCommandHandler : CommandHandler<RaceAggregate, RaceId, CreateRaceCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, CreateRaceCommand command, CancellationToken cancellationToken)
    {
        aggregate.Create(command.RaceDate, command.RacecourseCode, command.RaceNumber, command.RaceName,
            command.MeetingNumber, command.DayNumber, command.GradeCode,
            command.SurfaceCode, command.DistanceMeters, command.DirectionCode);
        return Task.CompletedTask;
    }
}

public sealed class PublishRaceCardCommand : Command<RaceAggregate, RaceId>
{
    public PublishRaceCardCommand(RaceId aggregateId, int entryCount)
        : base(aggregateId)
    {
        EntryCount = entryCount;
    }

    public int EntryCount { get; }
}

public sealed class PublishRaceCardCommandHandler : CommandHandler<RaceAggregate, RaceId, PublishRaceCardCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, PublishRaceCardCommand command, CancellationToken cancellationToken)
    {
        aggregate.PublishCard(command.EntryCount);
        return Task.CompletedTask;
    }
}

public sealed class RegisterEntryCommand : Command<RaceAggregate, RaceId>
{
    public RegisterEntryCommand(RaceId aggregateId, string entryId, string horseId, int horseNumber,
        string? jockeyId = null, string? trainerId = null,
        int? gateNumber = null, decimal? assignedWeight = null,
        string? sexCode = null, int? age = null,
        decimal? declaredWeight = null, decimal? declaredWeightDiff = null)
        : base(aggregateId)
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

public sealed class RegisterEntryCommandHandler : CommandHandler<RaceAggregate, RaceId, RegisterEntryCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, RegisterEntryCommand command, CancellationToken cancellationToken)
    {
        aggregate.RegisterEntry(command.EntryId, command.HorseId, command.HorseNumber,
            command.JockeyId, command.TrainerId, command.GateNumber, command.AssignedWeight,
            command.SexCode, command.Age, command.DeclaredWeight, command.DeclaredWeightDiff);
        return Task.CompletedTask;
    }
}

public sealed class RecordWeatherObservationCommand : Command<RaceAggregate, RaceId>
{
    public RecordWeatherObservationCommand(RaceId aggregateId, DateTimeOffset observationTime,
        string? weatherCode = null, string? weatherText = null,
        decimal? temperatureCelsius = null, decimal? humidityPercent = null,
        string? windDirectionCode = null, decimal? windSpeedMeterPerSecond = null)
        : base(aggregateId)
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

public sealed class RecordWeatherObservationCommandHandler : CommandHandler<RaceAggregate, RaceId, RecordWeatherObservationCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, RecordWeatherObservationCommand command, CancellationToken cancellationToken)
    {
        aggregate.RecordWeatherObservation(command.ObservationTime,
            command.WeatherCode, command.WeatherText, command.TemperatureCelsius, command.HumidityPercent,
            command.WindDirectionCode, command.WindSpeedMeterPerSecond);
        return Task.CompletedTask;
    }
}

public sealed class RecordTrackConditionObservationCommand : Command<RaceAggregate, RaceId>
{
    public RecordTrackConditionObservationCommand(RaceId aggregateId, DateTimeOffset observationTime,
        string? turfConditionCode = null, string? dirtConditionCode = null,
        string? goingDescriptionText = null)
        : base(aggregateId)
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

public sealed class RecordTrackConditionObservationCommandHandler : CommandHandler<RaceAggregate, RaceId, RecordTrackConditionObservationCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, RecordTrackConditionObservationCommand command, CancellationToken cancellationToken)
    {
        aggregate.RecordTrackConditionObservation(command.ObservationTime,
            command.TurfConditionCode, command.DirtConditionCode, command.GoingDescriptionText);
        return Task.CompletedTask;
    }
}

public sealed class OpenPreRaceCommand : Command<RaceAggregate, RaceId>
{
    public OpenPreRaceCommand(RaceId aggregateId) : base(aggregateId) { }
}

public sealed class OpenPreRaceCommandHandler : CommandHandler<RaceAggregate, RaceId, OpenPreRaceCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, OpenPreRaceCommand command, CancellationToken cancellationToken)
    {
        aggregate.OpenPreRace();
        return Task.CompletedTask;
    }
}

public sealed class StartRaceCommand : Command<RaceAggregate, RaceId>
{
    public StartRaceCommand(RaceId aggregateId) : base(aggregateId) { }
}

public sealed class StartRaceCommandHandler : CommandHandler<RaceAggregate, RaceId, StartRaceCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, StartRaceCommand command, CancellationToken cancellationToken)
    {
        aggregate.StartRace();
        return Task.CompletedTask;
    }
}

public sealed class DeclareRaceResultCommand : Command<RaceAggregate, RaceId>
{
    public DeclareRaceResultCommand(RaceId aggregateId, string winningHorseName, DateTimeOffset declaredAt,
        string? winningHorseId = null, string? stewardReportText = null)
        : base(aggregateId)
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

public sealed class DeclareRaceResultCommandHandler : CommandHandler<RaceAggregate, RaceId, DeclareRaceResultCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, DeclareRaceResultCommand command, CancellationToken cancellationToken)
    {
        aggregate.DeclareResult(command.WinningHorseName, command.DeclaredAt,
            command.WinningHorseId, command.StewardReportText);
        return Task.CompletedTask;
    }
}

public sealed class DeclareEntryResultCommand : Command<RaceAggregate, RaceId>
{
    public DeclareEntryResultCommand(RaceId aggregateId, string entryId,
        int? finishPosition = null, string? officialTime = null,
        string? marginText = null, string? lastThreeFurlongTime = null,
        string? abnormalResultCode = null, decimal? prizeMoney = null)
        : base(aggregateId)
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

public sealed class DeclareEntryResultCommandHandler : CommandHandler<RaceAggregate, RaceId, DeclareEntryResultCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, DeclareEntryResultCommand command, CancellationToken cancellationToken)
    {
        aggregate.DeclareEntryResult(command.EntryId, command.FinishPosition, command.OfficialTime,
            command.MarginText, command.LastThreeFurlongTime, command.AbnormalResultCode, command.PrizeMoney);
        return Task.CompletedTask;
    }
}

public sealed class DeclarePayoutResultCommand : Command<RaceAggregate, RaceId>
{
    public DeclarePayoutResultCommand(RaceId aggregateId, DateTimeOffset declaredAt,
        IReadOnlyList<PayoutEntry>? winPayouts = null,
        IReadOnlyList<PayoutEntry>? placePayouts = null,
        IReadOnlyList<PayoutEntry>? quinellaPayouts = null,
        IReadOnlyList<PayoutEntry>? exactaPayouts = null,
        IReadOnlyList<PayoutEntry>? trifectaPayouts = null)
        : base(aggregateId)
    {
        DeclaredAt = declaredAt;
        WinPayouts = winPayouts;
        PlacePayouts = placePayouts;
        QuinellaPayouts = quinellaPayouts;
        ExactaPayouts = exactaPayouts;
        TrifectaPayouts = trifectaPayouts;
    }

    public DateTimeOffset DeclaredAt { get; }
    public IReadOnlyList<PayoutEntry>? WinPayouts { get; }
    public IReadOnlyList<PayoutEntry>? PlacePayouts { get; }
    public IReadOnlyList<PayoutEntry>? QuinellaPayouts { get; }
    public IReadOnlyList<PayoutEntry>? ExactaPayouts { get; }
    public IReadOnlyList<PayoutEntry>? TrifectaPayouts { get; }
}

public sealed class DeclarePayoutResultCommandHandler : CommandHandler<RaceAggregate, RaceId, DeclarePayoutResultCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, DeclarePayoutResultCommand command, CancellationToken cancellationToken)
    {
        aggregate.DeclarePayoutResult(command.DeclaredAt, command.WinPayouts, command.PlacePayouts,
            command.QuinellaPayouts, command.ExactaPayouts, command.TrifectaPayouts);
        return Task.CompletedTask;
    }
}

public sealed class CloseRaceLifecycleCommand : Command<RaceAggregate, RaceId>
{
    public CloseRaceLifecycleCommand(RaceId aggregateId) : base(aggregateId) { }
}

public sealed class CloseRaceLifecycleCommandHandler : CommandHandler<RaceAggregate, RaceId, CloseRaceLifecycleCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, CloseRaceLifecycleCommand command, CancellationToken cancellationToken)
    {
        aggregate.CloseRaceLifecycle();
        return Task.CompletedTask;
    }
}

public sealed class CorrectRaceDataCommand : Command<RaceAggregate, RaceId>
{
    public CorrectRaceDataCommand(RaceId aggregateId, string? raceName = null, string? racecourseCode = null,
        int? raceNumber = null, string? gradeCode = null,
        string? surfaceCode = null, int? distanceMeters = null,
        string? directionCode = null, string? reason = null)
        : base(aggregateId)
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

public sealed class CorrectRaceDataCommandHandler : CommandHandler<RaceAggregate, RaceId, CorrectRaceDataCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, CorrectRaceDataCommand command, CancellationToken cancellationToken)
    {
        aggregate.CorrectRaceData(command.RaceName, command.RacecourseCode, command.RaceNumber,
            command.GradeCode, command.SurfaceCode, command.DistanceMeters, command.DirectionCode, command.Reason);
        return Task.CompletedTask;
    }
}
