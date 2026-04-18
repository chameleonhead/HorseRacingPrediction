using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Jockeys;

public sealed class JockeyRegistered : AggregateEvent<JockeyAggregate, JockeyId>
{
    public JockeyRegistered(string displayName, string normalizedName, string? affiliationCode = null)
    {
        DisplayName = displayName;
        NormalizedName = normalizedName;
        AffiliationCode = affiliationCode;
    }

    public string DisplayName { get; }
    public string NormalizedName { get; }
    public string? AffiliationCode { get; }
}
