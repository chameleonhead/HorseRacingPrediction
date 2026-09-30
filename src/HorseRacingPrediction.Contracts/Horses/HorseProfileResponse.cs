
using HorseRacingPrediction.Contracts.Common;

namespace HorseRacingPrediction.Contracts.Horses;

public sealed record HorseProfileResponse(
    string HorseId,
    string RegisteredName,
    string NormalizedName,
    string? SexCode,
    DateOnly? BirthDate,
    IReadOnlyList<AliasDto> Aliases,
    string? OwnerName = null,
    string? BreederName = null,
    string? SireName = null,
    string? DamName = null,
    string? DamsireName = null,
    string? CoatColor = null);
