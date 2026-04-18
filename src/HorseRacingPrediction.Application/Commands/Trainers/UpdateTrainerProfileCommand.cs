using EventFlow.Commands;
using HorseRacingPrediction.Domain.Trainers;

namespace HorseRacingPrediction.Application.Commands.Trainers;

public sealed class UpdateTrainerProfileCommand : Command<TrainerAggregate, TrainerId>
{
    public UpdateTrainerProfileCommand(TrainerId aggregateId, string? displayName = null,
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
