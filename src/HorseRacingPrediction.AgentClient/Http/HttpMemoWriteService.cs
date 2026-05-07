using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace HorseRacingPrediction.AgentClient.Http;

public sealed class HttpMemoWriteService : IMemoWriteService
{
    private readonly HttpClient _httpClient;

    public HttpMemoWriteService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string?> CreateRaceMemoAsync(
        string raceId,
        string memoType,
        string content,
        string authorId,
        string? memoId,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateMemoRequestDto(
            AuthorId: authorId,
            MemoType: memoType,
            Content: content,
            CreatedAt: DateTimeOffset.UtcNow,
            Subjects:
            [
                new MemoSubjectDto("Race", raceId)
            ],
            Links: null,
            MemoId: memoId);

        var response = await _httpClient
            .PostAsJsonAsync("/api/memos", request, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Memo作成に失敗しました: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
        }

        var payload = await response.Content
            .ReadFromJsonAsync<CreateMemoResponseDto>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return payload?.MemoId;
    }

    private sealed record CreateMemoRequestDto(
        string? AuthorId,
        string MemoType,
        string Content,
        DateTimeOffset CreatedAt,
        IReadOnlyList<MemoSubjectDto> Subjects,
        object? Links,
        string? MemoId);

    private sealed record MemoSubjectDto(string SubjectType, string SubjectId);

    private sealed class CreateMemoResponseDto
    {
        [JsonPropertyName("memoId")]
        public string? MemoId { get; init; }
    }
}
