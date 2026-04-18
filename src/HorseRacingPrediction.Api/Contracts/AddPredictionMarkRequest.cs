using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Api.Contracts;

public sealed record AddPredictionMarkRequest(
    [property: Required, StringLength(64, MinimumLength = 1)] string EntryId,
    [property: Required, StringLength(8, MinimumLength = 1)] string MarkCode,
    [property: Range(1, 40)] int PredictedRank,
    [property: Range(0, 100)] decimal Score,
    [property: StringLength(300)] string? Comment);
