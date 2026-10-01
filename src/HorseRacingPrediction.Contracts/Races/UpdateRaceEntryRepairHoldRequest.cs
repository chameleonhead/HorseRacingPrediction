using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record UpdateRaceEntryRepairHoldRequest(
    [property: JsonIgnore] string RaceId,
    UpdateRaceEntryRepairHoldInputDto Hold);
