using EventFlow.Commands;
using HorseRacingPrediction.Domain.Jockeys;

namespace HorseRacingPrediction.Application.Commands.Jockeys;

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
