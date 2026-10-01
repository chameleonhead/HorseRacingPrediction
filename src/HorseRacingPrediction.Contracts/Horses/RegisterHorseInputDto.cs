using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Contracts.Horses;

public sealed record RegisterHorseInputDto(
    [property: Required, StringLength(128, MinimumLength = 1)] string RegisteredName,
    [property: Required, StringLength(128, MinimumLength = 1)] string NormalizedName,
    string? SexCode,
    DateOnly? BirthDate,
    string? HorseId = null,
    string? OwnerName = null,
    string? BreederName = null,
    string? SireName = null,
    string? DamName = null,
    string? DamsireName = null,
    string? CoatColor = null);
