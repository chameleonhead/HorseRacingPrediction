
namespace HorseRacingPrediction.Contracts.Races;

public sealed record RaceEntryDto(
    string EntryId,
    string HorseId,
    string? HorseName,
    int? HorseNumber,
    string? JockeyId,
    string? JockeyName,
    string? TrainerId,
    string? TrainerName,
    int? GateNumber,
    decimal? AssignedWeight,
    string? SexCode,
    int? Age,
    decimal? DeclaredWeight,
    decimal? DeclaredWeightDiff,
    string? RunningStyleCode,
    string? OwnerName = null,
    string? OwnerId = null,
    HorseRacingPrediction.Contracts.Races.RaceEntryParticipationStatus ParticipationStatus = HorseRacingPrediction.Contracts.Races.RaceEntryParticipationStatus.Active);
