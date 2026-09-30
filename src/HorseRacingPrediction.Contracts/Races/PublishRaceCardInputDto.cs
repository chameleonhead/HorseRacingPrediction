using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record PublishRaceCardInputDto([property: Range(1, 40)] int EntryCount);
