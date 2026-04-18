using EventFlow.Commands;
using HorseRacingPrediction.Domain.Trainers;

namespace HorseRacingPrediction.Application.Commands.Trainers;

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
