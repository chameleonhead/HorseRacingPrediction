using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Api.Contracts;

public sealed record DeclareRaceResultRequest(
    [property: Required, StringLength(128, MinimumLength = 1)] string WinningHorseName,
    DateTimeOffset? DeclaredAt);
