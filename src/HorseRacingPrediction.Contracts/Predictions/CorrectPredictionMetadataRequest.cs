
using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Predictions;

public sealed record CorrectPredictionMetadataRequest(
    [property: JsonIgnore] string PredictionTicketId,
    CorrectPredictionMetadataInputDto? Metadata);
