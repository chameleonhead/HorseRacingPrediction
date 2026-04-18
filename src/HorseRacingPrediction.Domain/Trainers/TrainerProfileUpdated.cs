using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Trainers;

public sealed class TrainerProfileUpdated : AggregateEvent<TrainerAggregate, TrainerId>
{
    public TrainerProfileUpdated(string? displayName = null, string? normalizedName = null, string? affiliationCode = null)
    {
        DisplayName = displayName;
        NormalizedName = normalizedName;
        AffiliationCode = affiliationCode;
    }

    public string? DisplayName { get; }
    public string? NormalizedName { get; }
    public string? AffiliationCode { get; }
}
