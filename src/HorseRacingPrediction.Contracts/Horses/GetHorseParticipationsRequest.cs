using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Horses;

public sealed record GetHorseParticipationsRequest(
    [property: JsonIgnore] string HorseId,
    int? Take,
    int? Skip);
