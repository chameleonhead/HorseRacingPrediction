using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record DeclarePayoutResultRequest(DeclarePayoutResultInputDto? Payout)
{
    [JsonIgnore]
    public string RaceId { get; init; } = string.Empty;
}
