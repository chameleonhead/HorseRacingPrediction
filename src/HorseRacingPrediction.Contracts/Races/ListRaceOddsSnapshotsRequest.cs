using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record ListRaceOddsSnapshotsRequest(
    [property: JsonIgnore] string RaceId);
