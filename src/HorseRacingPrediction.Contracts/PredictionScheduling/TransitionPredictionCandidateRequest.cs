using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.PredictionScheduling;

public sealed record TransitionPredictionCandidateRequest(
    [property: JsonIgnore] string RaceId,
    PredictionCandidateTransitionInputDto? Transition);
