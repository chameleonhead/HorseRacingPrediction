using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Trainers;

public sealed class TrainerDataCorrected : AggregateEvent<TrainerAggregate, TrainerId>
{
    public TrainerDataCorrected(string? displayName = null, string? normalizedName = null,
        string? affiliationCode = null, string? reason = null)
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
