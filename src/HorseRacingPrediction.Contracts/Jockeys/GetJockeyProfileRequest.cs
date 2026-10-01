using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Jockeys;

public sealed record GetJockeyProfileRequest([property: JsonIgnore] string JockeyId);
