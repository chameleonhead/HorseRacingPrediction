namespace HorseRacingPrediction.Agents.Contracts;

public sealed record MlPredictionResponse(
    string RaceId,
    IReadOnlyList<MlHorsePrediction> Rankings);
