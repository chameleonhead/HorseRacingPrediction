using EventFlow.Commands;
using HorseRacingPrediction.Domain.Trainers;

namespace HorseRacingPrediction.Application.Commands.Trainers;

public sealed class RegisterTrainerCommand : Command<TrainerAggregate, TrainerId>
{
    public RegisterTrainerCommand(TrainerId aggregateId, string displayName, string normalizedName,
        string? affiliationCode = null)
        : base(aggregateId)
    {
        DisplayName = displayName;
        NormalizedName = normalizedName;
        AffiliationCode = affiliationCode;
    }

    public string DisplayName { get; }
    public string NormalizedName { get; }
    public string? AffiliationCode { get; }
}

public sealed class RegisterTrainerCommandHandler : CommandHandler<TrainerAggregate, TrainerId, RegisterTrainerCommand>
{
    public override Task ExecuteAsync(TrainerAggregate aggregate, RegisterTrainerCommand command, CancellationToken cancellationToken)
    {
        aggregate.RegisterTrainer(command.DisplayName, command.NormalizedName, command.AffiliationCode);
        return Task.CompletedTask;
    }
}

public sealed class UpdateTrainerProfileCommand : Command<TrainerAggregate, TrainerId>
{
    public UpdateTrainerProfileCommand(TrainerId aggregateId, string? displayName = null,
        string? normalizedName = null, string? affiliationCode = null)
        : base(aggregateId)
    {
        DisplayName = displayName;
        NormalizedName = normalizedName;
        AffiliationCode = affiliationCode;
    }

    public string? DisplayName { get; }
    public string? NormalizedName { get; }
    public string? AffiliationCode { get; }
}

public sealed class UpdateTrainerProfileCommandHandler : CommandHandler<TrainerAggregate, TrainerId, UpdateTrainerProfileCommand>
{
    public override Task ExecuteAsync(TrainerAggregate aggregate, UpdateTrainerProfileCommand command, CancellationToken cancellationToken)
    {
        aggregate.UpdateProfile(command.DisplayName, command.NormalizedName, command.AffiliationCode);
        return Task.CompletedTask;
    }
}

public sealed class MergeTrainerAliasCommand : Command<TrainerAggregate, TrainerId>
{
    public MergeTrainerAliasCommand(TrainerId aggregateId, string aliasType, string aliasValue, string sourceName, bool isPrimary)
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

public sealed class MergeTrainerAliasCommandHandler : CommandHandler<TrainerAggregate, TrainerId, MergeTrainerAliasCommand>
{
    public override Task ExecuteAsync(TrainerAggregate aggregate, MergeTrainerAliasCommand command, CancellationToken cancellationToken)
    {
        aggregate.MergeAlias(command.AliasType, command.AliasValue, command.SourceName, command.IsPrimary);
        return Task.CompletedTask;
    }
}

public sealed class CorrectTrainerDataCommand : Command<TrainerAggregate, TrainerId>
{
    public CorrectTrainerDataCommand(TrainerId aggregateId, string? displayName = null,
        string? normalizedName = null, string? affiliationCode = null, string? reason = null)
        : base(aggregateId)
    {
        DisplayName = displayName;
        NormalizedName = normalizedName;
        AffiliationCode = affiliationCode;
        Reason = reason;
    }

    public string? DisplayName { get; }
    public string? NormalizedName { get; }
    public string? AffiliationCode { get; }
    public string? Reason { get; }
}

public sealed class CorrectTrainerDataCommandHandler : CommandHandler<TrainerAggregate, TrainerId, CorrectTrainerDataCommand>
{
    public override Task ExecuteAsync(TrainerAggregate aggregate, CorrectTrainerDataCommand command, CancellationToken cancellationToken)
    {
        aggregate.CorrectData(command.DisplayName, command.NormalizedName, command.AffiliationCode, command.Reason);
        return Task.CompletedTask;
    }
}
