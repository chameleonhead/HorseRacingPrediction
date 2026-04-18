using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Api.Contracts;

public sealed record MergeAliasRequest(
    [property: Required, StringLength(32, MinimumLength = 1)] string AliasType,
    [property: Required, StringLength(128, MinimumLength = 1)] string AliasValue,
    [property: Required, StringLength(64, MinimumLength = 1)] string SourceName,
    bool IsPrimary);
