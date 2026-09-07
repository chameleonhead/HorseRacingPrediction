using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Contracts;

public sealed record PayoutEntryDto(
    [property: Required] string Combination,
    decimal Amount);
