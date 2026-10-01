using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Predictions;

public sealed record FinalizePredictionTicketRequest([property: JsonIgnore] string PredictionTicketId);
