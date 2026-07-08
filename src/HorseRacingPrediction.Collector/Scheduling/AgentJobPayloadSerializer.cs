using System.Text.Json;

namespace HorseRacingPrediction.Collector.Scheduling;

public static class AgentJobPayloadSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T payload)
        => JsonSerializer.Serialize(payload, JsonOptions);

    public static T Deserialize<T>(string payload)
        => JsonSerializer.Deserialize<T>(payload, JsonOptions)
           ?? throw new InvalidOperationException($"ジョブ payload を {typeof(T).Name} として復元できませんでした。");
}