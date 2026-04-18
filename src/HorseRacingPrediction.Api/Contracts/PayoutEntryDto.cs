using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Api.Contracts;

public sealed record PayoutEntryDto(
    [property: Required] string Combination,
    decimal Amount);
