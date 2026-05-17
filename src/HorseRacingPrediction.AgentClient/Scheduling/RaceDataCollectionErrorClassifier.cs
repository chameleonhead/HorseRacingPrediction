namespace HorseRacingPrediction.AgentClient.Scheduling;

public static class RaceDataCollectionErrorClassifier
{
    public static RaceDataCollectionErrorDescriptor Classify(string? message, Exception? exception = null)
    {
        if (exception is HttpRequestException)
        {
            return new RaceDataCollectionErrorDescriptor(
                RaceDataCollectionErrorCode.ExternalRequestFailed,
                string.IsNullOrWhiteSpace(message) ? exception.Message : message!);
        }

        var normalized = message?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new RaceDataCollectionErrorDescriptor(RaceDataCollectionErrorCode.Unknown, "Unknown error.");
        }

        if (normalized.Contains("未対応の ProviderType", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("not supported", StringComparison.OrdinalIgnoreCase))
        {
            return new RaceDataCollectionErrorDescriptor(RaceDataCollectionErrorCode.UnsupportedProvider, normalized);
        }

        if (normalized.Contains("開催日・競馬場・レース番号の特定に失敗", StringComparison.Ordinal)
            || normalized.Contains("metadata", StringComparison.OrdinalIgnoreCase))
        {
            return new RaceDataCollectionErrorDescriptor(RaceDataCollectionErrorCode.MetadataMissing, normalized);
        }

        if (normalized.Contains("ページを開けませんでした", StringComparison.Ordinal)
            || normalized.Contains("structured next link", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("navigate", StringComparison.OrdinalIgnoreCase))
        {
            return new RaceDataCollectionErrorDescriptor(RaceDataCollectionErrorCode.NavigationFailed, normalized);
        }

        if (normalized.Contains("発見", StringComparison.Ordinal)
            || normalized.Contains("discover", StringComparison.OrdinalIgnoreCase))
        {
            return new RaceDataCollectionErrorDescriptor(RaceDataCollectionErrorCode.DiscoveryFailed, normalized);
        }

        if (normalized.Contains("保存", StringComparison.Ordinal)
            || normalized.Contains("save", StringComparison.OrdinalIgnoreCase))
        {
            return new RaceDataCollectionErrorDescriptor(RaceDataCollectionErrorCode.SaveFailed, normalized);
        }

        if (normalized.Contains("winner", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("勝ち馬", StringComparison.Ordinal)
            || normalized.Contains("No card data", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("No result data", StringComparison.OrdinalIgnoreCase))
        {
            return new RaceDataCollectionErrorDescriptor(RaceDataCollectionErrorCode.NoDataFound, normalized);
        }

        if (normalized.Contains("scrape", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("抽出", StringComparison.Ordinal))
        {
            return new RaceDataCollectionErrorDescriptor(RaceDataCollectionErrorCode.ScrapeFailed, normalized);
        }

        return new RaceDataCollectionErrorDescriptor(RaceDataCollectionErrorCode.Unknown, normalized);
    }
}