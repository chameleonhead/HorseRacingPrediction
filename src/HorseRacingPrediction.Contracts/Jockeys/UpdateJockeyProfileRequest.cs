using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Jockeys;

public sealed record UpdateJockeyProfileRequest
{
    [JsonIgnore]
    public string JockeyId { get; init; } = string.Empty;

    public UpdateJockeyProfileInputDto? Jockey { get; init; }
}
