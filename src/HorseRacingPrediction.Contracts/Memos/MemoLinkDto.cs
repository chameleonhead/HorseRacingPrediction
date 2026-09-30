
namespace HorseRacingPrediction.Contracts.Memos;

public sealed record MemoLinkDto(
    string LinkId,
    string LinkType,
    string Title,
    string? Url,
    string? StorageKey);
