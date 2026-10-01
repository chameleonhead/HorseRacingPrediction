using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Owners;

public sealed record GetOwnerRequest(
    [property: JsonIgnore] string OwnerId,
    int? Take,
    int? Skip);
