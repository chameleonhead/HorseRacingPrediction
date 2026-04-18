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
