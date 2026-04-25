namespace HorseRacingPrediction.AgentClient.Http;

/// <summary>
/// クラウド API への接続設定。
/// </summary>
public sealed class ApiClientOptions
{
    public const string SectionName = "ApiClient";

    /// <summary>クラウド API のベース URL（例: https://api.example.com）</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>X-Api-Key ヘッダーに送信する API キー</summary>
    public string ApiKey { get; set; } = string.Empty;
}
