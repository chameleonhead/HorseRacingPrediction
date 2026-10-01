using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Horses;

public sealed record GetHorseRaceHistoryRequest([property: JsonIgnore] string HorseId);
