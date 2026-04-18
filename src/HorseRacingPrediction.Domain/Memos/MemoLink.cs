namespace HorseRacingPrediction.Domain.Memos;

public sealed record MemoLink(
    string LinkId,
    MemoLinkType LinkType,
    string Title,
    string? Url,
    string? StorageKey);
