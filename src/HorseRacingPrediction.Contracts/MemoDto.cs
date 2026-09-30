namespace HorseRacingPrediction.Contracts;

public sealed record MemoDto(
    string MemoId,
    string? AuthorId,
    string MemoType,
    string Content,
    DateTimeOffset CreatedAt,
    IReadOnlyList<MemoSubjectDto> Subjects,
    IReadOnlyList<MemoLinkDto> Links);
