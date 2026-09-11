using System.Net.Http.Json;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.Web.ApiBrowsing;

public sealed partial class AdminApiClient
{
    private const string CollectionPlatformPath = "/api/admin/collection";

    public Task<CollectionProgressSnapshot?> GetCollectionProgressAsync(CancellationToken token = default)
        => GetJsonAsync<CollectionProgressSnapshot>($"{CollectionPlatformPath}/progress", token);

    public Task<CollectionPipelineState?> GetCollectionPipelineAsync(CancellationToken token = default)
        => GetJsonAsync<CollectionPipelineState>($"{CollectionPlatformPath}/pipeline", token);

    public Task<CollectionStateSnapshot?> GetCollectionStateAsync(ResourceKey resource,
        CollectionDefinitionId definition, CancellationToken token = default)
        => GetJsonAsync<CollectionStateSnapshot>(
            $"{CollectionPlatformPath}/states/{resource.Type}/{Uri.EscapeDataString(resource.Provider)}" +
            $"/{Uri.EscapeDataString(resource.Id)}/{Uri.EscapeDataString(definition.Value)}", token);

    public Task<CollectionResourceDetail?> GetCollectionResourceDetailAsync(ResourceKey resource,
        CollectionDefinitionId definition, CancellationToken token = default)
        => GetJsonAsync<CollectionResourceDetail>(
            $"{CollectionPlatformPath}/resources/{resource.Type}/{Uri.EscapeDataString(resource.Provider)}" +
            $"/{Uri.EscapeDataString(resource.Id)}/{Uri.EscapeDataString(definition.Value)}", token);

    public Task<IReadOnlyList<BackfillBatchSnapshot>?> GetBackfillBatchesAsync(
        CancellationToken token = default)
        => GetJsonAsync<IReadOnlyList<BackfillBatchSnapshot>>($"{CollectionPlatformPath}/backfills", token);

    public Task<IReadOnlyList<CollectionTaskSummary>?> GetCollectionTasksAsync(
        CollectionTaskStatus? status = null, int limit = 200, CancellationToken token = default)
        => GetJsonAsync<IReadOnlyList<CollectionTaskSummary>>(
            $"{CollectionPlatformPath}/tasks?limit={Math.Clamp(limit, 1, 1000)}" +
            (status is null ? string.Empty : $"&status={status}"), token);

    public Task<IReadOnlyList<PendingCollectionFailureNotification>?> GetCollectionFailureNotificationsAsync(
        int limit = 100, CancellationToken token = default)
        => GetJsonAsync<IReadOnlyList<PendingCollectionFailureNotification>>(
            $"{CollectionPlatformPath}/failure-notifications?limit={Math.Clamp(limit, 1, 1000)}", token);

    public Task<AdminApiResult> PauseCollectionPipelineAsync(string? reason,
        CancellationToken token = default)
        => SendAsync(HttpMethod.Post, $"{CollectionPlatformPath}/pipeline/pause",
            new PauseCollectionPipelineRequest(reason), token);

    public Task<AdminApiResult> ResumeCollectionPipelineAsync(CancellationToken token = default)
        => SendAsync(HttpMethod.Post, $"{CollectionPlatformPath}/pipeline/resume", null, token);

    public Task<AdminApiResult> CancelCollectionTaskAsync(Guid taskId, CancellationToken token = default)
        => SendAsync(HttpMethod.Post, $"{CollectionPlatformPath}/tasks/{taskId:D}/cancel", null, token);

    public Task<AdminApiResult> MarkCollectionFailurePublishedAsync(Guid notificationId,
        CancellationToken token = default)
        => SendAsync(HttpMethod.Post,
            $"{CollectionPlatformPath}/failure-notifications/{notificationId:D}/published", null, token);

    public Task<AdminApiResult<CollectionRequestReceipt>> RequestCollectionAsync(
        CreateCollectionRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<CollectionRequestReceipt>(HttpMethod.Post,
            $"{CollectionPlatformPath}/requests", request, token);

    public Task<AdminApiResult<CollectionBulkPreview>> PreviewBulkCollectionAsync(
        BulkCollectionOperationRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<CollectionBulkPreview>(HttpMethod.Post,
            $"{CollectionPlatformPath}/requests/bulk/preview", request, token);

    public Task<AdminApiResult<CollectionBulkExecution>> ExecuteBulkCollectionAsync(
        BulkCollectionOperationRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<CollectionBulkExecution>(HttpMethod.Post,
            $"{CollectionPlatformPath}/requests/bulk", request, token);

    private async Task<AdminApiResult<T>> SendCollectionPlatformAsync<T>(HttpMethod method, string path,
        object body, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        using var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return AdminApiResult<T>.Fail(await ReadErrorsAsync(response, token).ConfigureAwait(false));
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, token).ConfigureAwait(false);
        return value is null
            ? AdminApiResult<T>.Fail(["応答の解析に失敗しました。"])
            : AdminApiResult<T>.Ok(value);
    }
}
