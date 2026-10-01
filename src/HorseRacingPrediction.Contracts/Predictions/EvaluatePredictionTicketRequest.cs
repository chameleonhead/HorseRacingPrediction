using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Predictions;

public sealed record EvaluatePredictionTicketRequest(
    [property: JsonIgnore] string PredictionTicketId,
    PredictionEvaluationInputDto? Evaluation);
