namespace HorseRacingPrediction.ApiClient.Contracts;

public sealed record MlHorsePrediction(
    string EntryId,
    string HorseId,
    int HorseNumber,
    float PredictedScore,
    int PredictedRank);
