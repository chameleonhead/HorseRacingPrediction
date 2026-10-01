using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Predictions;

public sealed record AddPredictionMarkRequest(
    [property: JsonIgnore] string PredictionTicketId,
    AddPredictionMarkInputDto? Mark);
