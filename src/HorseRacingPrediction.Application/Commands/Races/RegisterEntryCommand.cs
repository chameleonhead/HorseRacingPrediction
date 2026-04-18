using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class RegisterEntryCommand : Command<RaceAggregate, RaceId>
{
    public RegisterEntryCommand(RaceId aggregateId, string entryId, string horseId, int horseNumber,
        string? jockeyId = null, string? trainerId = null,
        int? gateNumber = null, decimal? assignedWeight = null,
        string? sexCode = null, int? age = null,
        decimal? declaredWeight = null, decimal? declaredWeightDiff = null)
        : base(aggregateId)
    {
        EntryId = entryId;
        HorseId = horseId;
        HorseNumber = horseNumber;
        JockeyId = jockeyId;
        TrainerId = trainerId;
        GateNumber = gateNumber;
        AssignedWeight = assignedWeight;
        SexCode = sexCode;
        Age = age;
        DeclaredWeight = declaredWeight;
        DeclaredWeightDiff = declaredWeightDiff;
    }

    public string EntryId { get; }
    public string HorseId { get; }
    public int HorseNumber { get; }
    public string? JockeyId { get; }
    public string? TrainerId { get; }
    public int? GateNumber { get; }
    public decimal? AssignedWeight { get; }
    public string? SexCode { get; }
    public int? Age { get; }
    public decimal? DeclaredWeight { get; }
    public decimal? DeclaredWeightDiff { get; }
}
