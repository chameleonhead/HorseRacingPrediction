
using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record DeclareEntryResultRequest(DeclareEntryResultInputDto? Result)
{
    [JsonIgnore]
    public string RaceId { get; init; } = string.Empty;

    [JsonIgnore]
    public string EntryId { get; init; } = string.Empty;
}
