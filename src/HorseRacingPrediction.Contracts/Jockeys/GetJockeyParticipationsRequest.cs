using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Jockeys;

public sealed record GetJockeyParticipationsRequest(
    [property: JsonIgnore] string JockeyId,
    int? Take,
    int? Skip);
