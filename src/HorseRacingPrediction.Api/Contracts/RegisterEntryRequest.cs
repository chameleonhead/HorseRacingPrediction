using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Api.Contracts;

public sealed record RegisterEntryRequest(
    [property: Required, StringLength(64, MinimumLength = 1)] string HorseId,
    [property: Range(1, 40)] int? HorseNumber,
    string? JockeyId,
    string? TrainerId,
    [property: Range(1, 8)] int? GateNumber,
    decimal? AssignedWeight,
    string? SexCode,
    int? Age,
    decimal? DeclaredWeight,
    decimal? DeclaredWeightDiff,
    string? RunningStyleCode = null,
    string? EntryId = null,
    string? HorseName = null,
    string? JockeyName = null,
    string? TrainerName = null,
    string? OwnerName = null,
    string? HorseSourceIdentity = null,
    HorseRacingPrediction.Contracts.RaceEntryParticipationStatus? ParticipationStatus = null);
