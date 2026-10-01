namespace HorseRacingPrediction.Contracts.Memos;

public sealed record UpdateMemoInputDto(
    string? MemoType = null,
    string? Content = null,
    IReadOnlyList<MemoLinkDto>? Links = null);
