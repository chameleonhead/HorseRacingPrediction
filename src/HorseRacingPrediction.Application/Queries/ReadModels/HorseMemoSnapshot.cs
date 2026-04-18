namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed record HorseMemoSnapshot(
    string MemoId,
    string? AuthorId,
    string MemoType,
    string Content,
    DateTimeOffset CreatedAt,
    IReadOnlyList<HorseMemoLinkSnapshot> Links);
