using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Contracts;

public sealed record DeclareRaceResultRequest(
    [property: Required, StringLength(128, MinimumLength = 1)] string WinningHorseName,
    DateTimeOffset? DeclaredAt);
