namespace HorseRacingPrediction.ApiClient.Contracts;

public sealed record MemoLinkSnapshot(
    string LinkId,
    string LinkType,
    string Title,
    string? Url,
    string? StorageKey);