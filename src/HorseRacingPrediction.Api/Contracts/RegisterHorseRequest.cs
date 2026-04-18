using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Api.Contracts;

public sealed record RegisterHorseRequest(
    [property: Required, StringLength(128, MinimumLength = 1)] string RegisteredName,
    [property: Required, StringLength(128, MinimumLength = 1)] string NormalizedName,
    string? SexCode,
    DateOnly? BirthDate,
    string? HorseId = null);
