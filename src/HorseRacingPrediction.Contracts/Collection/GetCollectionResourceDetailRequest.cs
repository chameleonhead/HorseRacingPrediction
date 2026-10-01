using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Collection;

public sealed record GetCollectionResourceDetailRequest
{
    [JsonIgnore] public CollectionResourceType Type { get; init; }
    [JsonIgnore] public string Provider { get; init; } = string.Empty;
    [JsonIgnore] public string ResourceId { get; init; } = string.Empty;
    [JsonIgnore] public string Definition { get; init; } = string.Empty;
    public int? HistoryPage { get; init; }
    public int? RequestHistoryPage { get; init; }
    public int? TaskHistoryPage { get; init; }
    public int? AttemptHistoryPage { get; init; }
    public int? HistoryPageSize { get; init; }
}
