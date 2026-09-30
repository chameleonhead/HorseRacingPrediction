namespace HorseRacingPrediction.Contracts;

public sealed record MlPredictionDto(
    string RaceId,
    IReadOnlyList<MlHorsePredictionDto> Rankings);
