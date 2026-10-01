using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Horses;

public sealed record GetHorseProfileRequest([property: JsonIgnore] string HorseId);
