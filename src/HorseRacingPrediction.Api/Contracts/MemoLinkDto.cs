namespace HorseRacingPrediction.Api.Contracts;

public sealed record MemoLinkDto(
    string LinkId,
    string LinkType,
    string Title,
    string? Url,
    string? StorageKey);
