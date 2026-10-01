using System.Net.Http.Json;

using HorseRacingPrediction.Contracts.Memos;

namespace HorseRacingPrediction.Collector.Http;

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
        var request = new CreateMemoRequest(new CreateMemoInputDto(
            AuthorId: authorId,
            MemoType: memoType,
            Content: content,
            CreatedAt: HorseRacingPrediction.Contracts.Common.Time.JstTime.Now(),
            Subjects:
            [
                new MemoSubjectDto("Race", raceId)
            ],
            Links: null,
            MemoId: memoId));

        var response = await _httpClient
            .PostAsJsonAsync("/api/memos", request, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Memo作成に失敗しました: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
        }

        var payload = await response.Content
            .ReadFromJsonAsync<CreateMemoResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return payload?.MemoId;
    }

    public async Task<string> CreateOrUpdateRaceMemoAsync(
        string raceId,
        string memoType,
        string content,
        string authorId,
        string memoId,
        CancellationToken cancellationToken = default)
    {
        var createRequest = new CreateMemoRequest(new CreateMemoInputDto(
            AuthorId: authorId,
            MemoType: memoType,
            Content: content,
            CreatedAt: HorseRacingPrediction.Contracts.Common.Time.JstTime.Now(),
            Subjects: [new MemoSubjectDto("Race", raceId)],
            Links: null,
            MemoId: memoId));

        var createResponse = await _httpClient
            .PostAsJsonAsync("/api/memos", createRequest, cancellationToken)
            .ConfigureAwait(false);

        if (createResponse.StatusCode != System.Net.HttpStatusCode.Conflict)
        {
            if (!createResponse.IsSuccessStatusCode)
            {
                var body = await createResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException($"Memo作成に失敗しました: {(int)createResponse.StatusCode} {createResponse.ReasonPhrase} {body}");
            }

            return memoId;
        }

        var updateRequest = new UpdateMemoRequest(memoId,
            new UpdateMemoInputDto(MemoType: null, Content: content, Links: null));
        var updateResponse = await _httpClient
            .PutAsJsonAsync($"/api/memos/{Uri.EscapeDataString(memoId)}", updateRequest, cancellationToken)
            .ConfigureAwait(false);

        if (!updateResponse.IsSuccessStatusCode)
        {
            var body = await updateResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Memo更新に失敗しました: {(int)updateResponse.StatusCode} {updateResponse.ReasonPhrase} {body}");
        }

        return memoId;
    }

}
