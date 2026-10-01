using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Jockeys;

public sealed record CorrectJockeyDataRequest
{
    [JsonIgnore]
    public string JockeyId { get; init; } = string.Empty;

    public CorrectJockeyDataInputDto? Jockey { get; init; }
}
