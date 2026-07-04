namespace HorseRacingPrediction.ApiClient.Contracts;

public sealed record MemoSnapshot(
    string MemoId,
    string? AuthorId,
    string MemoType,
    string Content,
    DateTimeOffset CreatedAt,
    List<MemoSubjectSnapshot> Subjects,
    List<MemoLinkSnapshot> Links);