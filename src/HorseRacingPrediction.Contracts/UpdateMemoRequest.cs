namespace HorseRacingPrediction.Contracts;

public sealed record UpdateMemoRequest(
    string? MemoType = null,
    string? Content = null,
    IReadOnlyList<MemoLinkDto>? Links = null);
