using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record PreviewRaceEntryRepairRequest(
    [property: JsonIgnore] string RaceId,
    RaceEntryRepairManifestInputDto Manifest);
