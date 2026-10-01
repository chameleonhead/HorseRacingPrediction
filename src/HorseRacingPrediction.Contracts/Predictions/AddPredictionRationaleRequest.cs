using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Predictions;

public sealed record AddPredictionRationaleRequest(
    [property: JsonIgnore] string PredictionTicketId,
    AddPredictionRationaleInputDto? Rationale);
