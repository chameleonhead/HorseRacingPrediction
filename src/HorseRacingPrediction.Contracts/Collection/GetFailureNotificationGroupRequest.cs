using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Collection;

public sealed record GetFailureNotificationGroupRequest
{
    [JsonIgnore] public string GroupKey { get; init; } = string.Empty;
    public string? Search { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
}
