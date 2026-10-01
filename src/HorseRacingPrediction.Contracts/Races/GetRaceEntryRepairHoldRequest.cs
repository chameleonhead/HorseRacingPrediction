using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record GetRaceEntryRepairHoldRequest(
    [property: JsonIgnore] string RaceId);
