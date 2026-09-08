using EventFlow.Commands;
using HorseRacingPrediction.Domain;
using HorseRacingPrediction.Domain.Horses;
namespace HorseRacingPrediction.Application.Commands.Horses;

public sealed class CollectHorseProfileCommand(HorseId id, CollectedSubjectProfile profile) : Command<HorseAggregate, HorseId>(id)
{
    public CollectedSubjectProfile Profile { get; } = profile;
}
