using EventFlow.Commands;
using HorseRacingPrediction.Domain.Horses;

namespace HorseRacingPrediction.Application.Commands.Horses;

public sealed class RegisterHorseCommand : Command<HorseAggregate, HorseId>
{
    public RegisterHorseCommand(HorseId aggregateId, string registeredName, string normalizedName,
        string? sexCode = null, DateOnly? birthDate = null)
        : base(aggregateId)
    {
        RegisteredName = registeredName;
        NormalizedName = normalizedName;
        SexCode = sexCode;
        BirthDate = birthDate;
    }

    public string RegisteredName { get; }
    public string NormalizedName { get; }
    public string? SexCode { get; }
    public DateOnly? BirthDate { get; }
}

public sealed class RegisterHorseCommandHandler : CommandHandler<HorseAggregate, HorseId, RegisterHorseCommand>
{
    public override Task ExecuteAsync(HorseAggregate aggregate, RegisterHorseCommand command, CancellationToken cancellationToken)
    {
        aggregate.RegisterHorse(command.RegisteredName, command.NormalizedName, command.SexCode, command.BirthDate);
        return Task.CompletedTask;
    }
}

public sealed class UpdateHorseProfileCommand : Command<HorseAggregate, HorseId>
{
    public UpdateHorseProfileCommand(HorseId aggregateId, string? registeredName = null,
        string? normalizedName = null, string? sexCode = null, DateOnly? birthDate = null)
        : base(aggregateId)
    {
        RegisteredName = registeredName;
        NormalizedName = normalizedName;
        SexCode = sexCode;
        BirthDate = birthDate;
    }

    public string? RegisteredName { get; }
    public string? NormalizedName { get; }
    public string? SexCode { get; }
    public DateOnly? BirthDate { get; }
}

public sealed class UpdateHorseProfileCommandHandler : CommandHandler<HorseAggregate, HorseId, UpdateHorseProfileCommand>
{
    public override Task ExecuteAsync(HorseAggregate aggregate, UpdateHorseProfileCommand command, CancellationToken cancellationToken)
    {
        aggregate.UpdateProfile(command.RegisteredName, command.NormalizedName, command.SexCode, command.BirthDate);
        return Task.CompletedTask;
    }
}

public sealed class MergeHorseAliasCommand : Command<HorseAggregate, HorseId>
{
    public MergeHorseAliasCommand(HorseId aggregateId, string aliasType, string aliasValue, string sourceName, bool isPrimary)
        : base(aggregateId)
    {
        AliasType = aliasType;
        AliasValue = aliasValue;
        SourceName = sourceName;
        IsPrimary = isPrimary;
    }

    public string AliasType { get; }
    public string AliasValue { get; }
    public string SourceName { get; }
    public bool IsPrimary { get; }
}

public sealed class MergeHorseAliasCommandHandler : CommandHandler<HorseAggregate, HorseId, MergeHorseAliasCommand>
{
    public override Task ExecuteAsync(HorseAggregate aggregate, MergeHorseAliasCommand command, CancellationToken cancellationToken)
    {
        aggregate.MergeAlias(command.AliasType, command.AliasValue, command.SourceName, command.IsPrimary);
        return Task.CompletedTask;
    }
}

public sealed class CorrectHorseDataCommand : Command<HorseAggregate, HorseId>
{
    public CorrectHorseDataCommand(HorseId aggregateId, string? registeredName = null,
        string? normalizedName = null, string? sexCode = null, DateOnly? birthDate = null, string? reason = null)
        : base(aggregateId)
    {
        RegisteredName = registeredName;
        NormalizedName = normalizedName;
        SexCode = sexCode;
        BirthDate = birthDate;
        Reason = reason;
    }

    public string? RegisteredName { get; }
    public string? NormalizedName { get; }
    public string? SexCode { get; }
    public DateOnly? BirthDate { get; }
    public string? Reason { get; }
}

public sealed class CorrectHorseDataCommandHandler : CommandHandler<HorseAggregate, HorseId, CorrectHorseDataCommand>
{
    public override Task ExecuteAsync(HorseAggregate aggregate, CorrectHorseDataCommand command, CancellationToken cancellationToken)
    {
        aggregate.CorrectData(command.RegisteredName, command.NormalizedName, command.SexCode, command.BirthDate, command.Reason);
        return Task.CompletedTask;
    }
}
