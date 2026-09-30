
namespace HorseRacingPrediction.Contracts.MachineLearning;

public sealed record MlPredictionDto(
    string RaceId,
    IReadOnlyList<MlHorsePredictionDto> Rankings);
