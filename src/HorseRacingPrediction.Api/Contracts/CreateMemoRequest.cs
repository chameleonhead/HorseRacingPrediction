namespace HorseRacingPrediction.Api.Contracts;

public sealed record CreateMemoRequest(
    string? AuthorId,
    string MemoType,
    string Content,
    DateTimeOffset CreatedAt,
    IReadOnlyList<MemoSubjectDto> Subjects,
    IReadOnlyList<MemoLinkDto>? Links = null,
    string? MemoId = null);
