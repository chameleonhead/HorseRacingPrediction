using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Jockeys;

public sealed class JockeyProfileUpdated : AggregateEvent<JockeyAggregate, JockeyId>
{
    public JockeyProfileUpdated(string? displayName = null, string? normalizedName = null, string? affiliationCode = null)
    {
        DisplayName = displayName;
        NormalizedName = normalizedName;
        AffiliationCode = affiliationCode;
    }

    public string? DisplayName { get; }
    public string? NormalizedName { get; }
    public string? AffiliationCode { get; }
}
