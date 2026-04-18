using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Trainers;

public sealed class TrainerRegistered : AggregateEvent<TrainerAggregate, TrainerId>
{
    public TrainerRegistered(string displayName, string normalizedName, string? affiliationCode = null)
    {
        DisplayName = displayName;
        NormalizedName = normalizedName;
        AffiliationCode = affiliationCode;
    }

    public string DisplayName { get; }
    public string NormalizedName { get; }
    public string? AffiliationCode { get; }
}
