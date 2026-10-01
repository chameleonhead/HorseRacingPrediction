using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record GetPredictionComparisonRequest(
    [property: JsonIgnore] string RaceId);
