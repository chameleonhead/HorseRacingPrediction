namespace HorseRacingPrediction.Api.Contracts;

/// <summary>ML予測レスポンス（レース全体）</summary>
public sealed record MlPredictionResponse(
    string RaceId,
    IReadOnlyList<MlHorsePredictionDto> Rankings);

/// <summary>ML予測レスポンス（1頭分）</summary>
public sealed record MlHorsePredictionDto(
    string EntryId,
    string HorseId,
    int HorseNumber,
    float PredictedScore,
    int PredictedRank);
