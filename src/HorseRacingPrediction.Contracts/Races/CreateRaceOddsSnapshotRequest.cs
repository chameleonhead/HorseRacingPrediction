using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record CreateRaceOddsSnapshotRequest(
    [property: JsonIgnore] string RaceId,
    CreateRaceOddsSnapshotInputDto Snapshot);
