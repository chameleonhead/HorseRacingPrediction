using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Contracts;

public sealed record PublishRaceCardRequest(
    [property: Range(1, 40)] int EntryCount);
