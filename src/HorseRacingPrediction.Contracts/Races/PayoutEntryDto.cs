using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record PayoutEntryDto(
    [property: Required] string Combination,
    decimal Amount);
