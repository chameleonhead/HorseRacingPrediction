using EventFlow.Commands;
using HorseRacingPrediction.Domain.Jockeys;

namespace HorseRacingPrediction.Application.Commands.Jockeys;

public sealed class RegisterJockeyCommand : Command<JockeyAggregate, JockeyId>
{
    public RegisterJockeyCommand(JockeyId aggregateId, string displayName, string normalizedName,
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

public sealed class RegisterJockeyCommandHandler : CommandHandler<JockeyAggregate, JockeyId, RegisterJockeyCommand>
{
    public override Task ExecuteAsync(JockeyAggregate aggregate, RegisterJockeyCommand command, CancellationToken cancellationToken)
    {
        aggregate.RegisterJockey(command.DisplayName, command.NormalizedName, command.AffiliationCode);
        return Task.CompletedTask;
    }
}

public sealed class UpdateJockeyProfileCommand : Command<JockeyAggregate, JockeyId>
{
    public UpdateJockeyProfileCommand(JockeyId aggregateId, string? displayName = null,
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

public sealed class UpdateJockeyProfileCommandHandler : CommandHandler<JockeyAggregate, JockeyId, UpdateJockeyProfileCommand>
{
    public override Task ExecuteAsync(JockeyAggregate aggregate, UpdateJockeyProfileCommand command, CancellationToken cancellationToken)
    {
        aggregate.UpdateProfile(command.DisplayName, command.NormalizedName, command.AffiliationCode);
        return Task.CompletedTask;
    }
}

public sealed class MergeJockeyAliasCommand : Command<JockeyAggregate, JockeyId>
{
    public MergeJockeyAliasCommand(JockeyId aggregateId, string aliasType, string aliasValue, string sourceName, bool isPrimary)
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

public sealed class MergeJockeyAliasCommandHandler : CommandHandler<JockeyAggregate, JockeyId, MergeJockeyAliasCommand>
{
    public override Task ExecuteAsync(JockeyAggregate aggregate, MergeJockeyAliasCommand command, CancellationToken cancellationToken)
    {
        aggregate.MergeAlias(command.AliasType, command.AliasValue, command.SourceName, command.IsPrimary);
        return Task.CompletedTask;
    }
}

public sealed class CorrectJockeyDataCommand : Command<JockeyAggregate, JockeyId>
{
    public CorrectJockeyDataCommand(JockeyId aggregateId, string? displayName = null,
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

public sealed class CorrectJockeyDataCommandHandler : CommandHandler<JockeyAggregate, JockeyId, CorrectJockeyDataCommand>
{
    public override Task ExecuteAsync(JockeyAggregate aggregate, CorrectJockeyDataCommand command, CancellationToken cancellationToken)
    {
        aggregate.CorrectData(command.DisplayName, command.NormalizedName, command.AffiliationCode, command.Reason);
        return Task.CompletedTask;
    }
}
