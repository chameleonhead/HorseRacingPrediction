using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Horses;

public sealed record GetHorseWeightHistoryRequest([property: JsonIgnore] string HorseId);
