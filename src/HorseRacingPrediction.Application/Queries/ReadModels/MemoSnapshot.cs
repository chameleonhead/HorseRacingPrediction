namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed record MemoSnapshot(
    string MemoId,
    string? AuthorId,
    string MemoType,
    string Content,
    DateTimeOffset CreatedAt,
    IReadOnlyList<MemoSubjectSnapshot> Subjects,
    IReadOnlyList<MemoLinkSnapshot> Links);
