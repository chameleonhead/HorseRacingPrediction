using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Predictions;

public sealed record RecalculatePredictionEvaluationRequest(
    [property: JsonIgnore] string PredictionTicketId,
    PredictionEvaluationInputDto? Evaluation);
