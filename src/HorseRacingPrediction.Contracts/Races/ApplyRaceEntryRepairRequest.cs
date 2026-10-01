using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record ApplyRaceEntryRepairRequest(
    [property: JsonIgnore] string RaceId,
    string OperationId,
    string Fingerprint,
    RaceEntryRepairManifestInputDto Manifest);
