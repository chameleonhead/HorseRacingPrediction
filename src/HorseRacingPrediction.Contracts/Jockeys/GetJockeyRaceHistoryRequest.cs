using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Jockeys;

public sealed record GetJockeyRaceHistoryRequest([property: JsonIgnore] string JockeyId);
