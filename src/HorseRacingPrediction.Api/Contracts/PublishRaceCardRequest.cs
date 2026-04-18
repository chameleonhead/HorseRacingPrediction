using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Api.Contracts;

public sealed record PublishRaceCardRequest(
    [property: Range(1, 40)] int EntryCount);
