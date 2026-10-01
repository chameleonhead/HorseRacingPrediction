using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Predictions;

public sealed record GetPredictionTicketRequest([property: JsonIgnore] string PredictionTicketId);
