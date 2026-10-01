using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record GetRaceAssignmentFenceRequest(
    [property: JsonIgnore] string RaceId);
