namespace HorseRacingPrediction.Api.Contracts;

public sealed record MemoResponse(
    string MemoId,
    string? AuthorId,
    string MemoType,
    string Content,
    DateTimeOffset CreatedAt,
    IReadOnlyList<MemoSubjectDto> Subjects,
    IReadOnlyList<MemoLinkDto> Links);
