using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record ReleaseRaceEntryRepairHoldRequest(
    [property: JsonIgnore] string RaceId,
    ReleaseRaceEntryRepairHoldInputDto Release);
