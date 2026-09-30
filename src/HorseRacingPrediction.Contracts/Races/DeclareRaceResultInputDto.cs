using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record DeclareRaceResultInputDto(
    [property: Required, StringLength(128, MinimumLength = 1)] string WinningHorseName,
    DateTimeOffset? DeclaredAt);
