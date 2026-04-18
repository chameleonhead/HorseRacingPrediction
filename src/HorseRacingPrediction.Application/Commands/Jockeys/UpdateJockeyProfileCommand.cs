using EventFlow.Commands;
using HorseRacingPrediction.Domain.Jockeys;

namespace HorseRacingPrediction.Application.Commands.Jockeys;

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
