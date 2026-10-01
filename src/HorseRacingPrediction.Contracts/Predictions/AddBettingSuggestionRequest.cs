using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Predictions;

public sealed record AddBettingSuggestionRequest(
    [property: JsonIgnore] string PredictionTicketId,
    AddBettingSuggestionInputDto? Suggestion);
