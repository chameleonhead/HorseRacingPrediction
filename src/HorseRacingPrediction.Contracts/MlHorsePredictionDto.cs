namespace HorseRacingPrediction.Contracts;

public sealed record MlHorsePredictionDto(
    string EntryId,
    string HorseId,
    int HorseNumber,
    float PredictedScore,
    int PredictedRank);
