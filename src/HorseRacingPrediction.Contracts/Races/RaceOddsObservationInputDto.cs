
namespace HorseRacingPrediction.Contracts.Races;

public sealed record RaceOddsObservationInputDto(string Market, string Selection, decimal Value,
    int? Popularity = null);
