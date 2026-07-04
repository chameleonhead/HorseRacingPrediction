namespace HorseRacingPrediction.ApiClient.Contracts;

public sealed record MlPredictionResponse(
    string RaceId,
    IReadOnlyList<MlHorsePrediction> Rankings);
