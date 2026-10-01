using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record GetRaceEntryRepairRequest(
    [property: JsonIgnore] string RaceId);
