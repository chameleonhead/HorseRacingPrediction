using System.Net.Http.Json;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.Web.ApiBrowsing;

public sealed partial class AdminApiClient
{
    private const string CollectionPlatformPath = "/api/admin/collection";

    public Task<CollectionProgressSnapshot?> GetCollectionProgressAsync(CancellationToken token = default)
        => GetJsonAsync<CollectionProgressSnapshot>($"{CollectionPlatformPath}/progress", token);

    public Task<CollectionTaskViewCounts?> GetCollectionTaskViewCountsAsync(CancellationToken token = default)
        => GetJsonAsync<CollectionTaskViewCounts>($"{CollectionPlatformPath}/task-view-counts", token);

    public Task<CollectionOperationsDashboard?> GetCollectionOperationsDashboardAsync(CancellationToken token = default)
        => GetJsonAsync<CollectionOperationsDashboard>($"{CollectionPlatformPath}/dashboard", token);

    public Task<CollectionPipelineState?> GetCollectionPipelineAsync(CancellationToken token = default)
        => GetJsonAsync<CollectionPipelineState>($"{CollectionPlatformPath}/pipeline", token);

    public Task<CollectionStateSnapshot?> GetCollectionStateAsync(ResourceKey resource,
        CollectionDefinitionId definition, CancellationToken token = default)
        => GetJsonAsync<CollectionStateSnapshot>(
            $"{CollectionPlatformPath}/states/{resource.Type}/{Uri.EscapeDataString(resource.Provider)}" +
            $"/{Uri.EscapeDataString(resource.Id)}/{Uri.EscapeDataString(definition.Value)}", token);

    public Task<CollectionResourceDetail?> GetCollectionResourceDetailAsync(ResourceKey resource,
        CollectionDefinitionId definition, int requestHistoryPage = 1, int taskHistoryPage = 1,
        int attemptHistoryPage = 1, int historyPageSize = 25,
        CancellationToken token = default)
        => GetJsonAsync<CollectionResourceDetail>(
            $"{CollectionPlatformPath}/resources/{resource.Type}/{Uri.EscapeDataString(resource.Provider)}" +
            $"/{Uri.EscapeDataString(resource.Id)}/{Uri.EscapeDataString(definition.Value)}" +
            $"?requestHistoryPage={Math.Max(1, requestHistoryPage)}" +
            $"&taskHistoryPage={Math.Max(1, taskHistoryPage)}" +
            $"&attemptHistoryPage={Math.Max(1, attemptHistoryPage)}" +
            $"&historyPageSize={Math.Clamp(historyPageSize, 1, 100)}", token);

    public Task<IReadOnlyList<BackfillBatchSnapshot>?> GetBackfillBatchesAsync(
        CancellationToken token = default)
        => GetJsonAsync<IReadOnlyList<BackfillBatchSnapshot>>($"{CollectionPlatformPath}/backfills", token);

    public Task<BackfillBatchSnapshot?> GetBackfillBatchAsync(string batchId, CancellationToken token = default)
        => GetJsonAsync<BackfillBatchSnapshot>($"{CollectionPlatformPath}/backfills/{Uri.EscapeDataString(batchId)}", token);

    public Task<AdminApiResult<BackfillHoleRecoveryResult>> RecoverBackfillHolesAsync(string batchId,
        CancellationToken token = default)
        => SendCollectionPlatformAsync<BackfillHoleRecoveryResult>(HttpMethod.Post,
            $"{CollectionPlatformPath}/backfills/{Uri.EscapeDataString(batchId)}/recover-holes", new { }, token);

    public Task<IReadOnlyList<CollectionTaskSummary>?> GetCollectionTasksAsync(
        CollectionTaskStatus? status = null, int limit = 200, CancellationToken token = default)
        => GetJsonAsync<IReadOnlyList<CollectionTaskSummary>>(
            $"{CollectionPlatformPath}/tasks?limit={Math.Clamp(limit, 1, 1000)}" +
            (status is null ? string.Empty : $"&status={status}"), token);

    public Task<CollectionExecutionBatchDetail?> GetCollectionExecutionBatchAsync(Guid executionBatchId,
        CancellationToken token = default)
        => GetJsonAsync<CollectionExecutionBatchDetail>(
            $"{CollectionPlatformPath}/execution-batches/{executionBatchId:D}", token);

    public Task<CollectionTaskPage?> SearchCollectionTasksAsync(CollectionTaskQuery query,
        CancellationToken token = default)
    {
        var values = new Dictionary<string, string?>
        {
            ["statuses"] = query.Statuses is null ? null : string.Join(',', query.Statuses),
            ["resourceType"] = query.ResourceType?.ToString(),
            ["provider"] = query.Provider,
            ["definitionId"] = query.DefinitionId,
            ["lane"] = query.Lane?.ToString(),
            ["search"] = query.Search,
            ["createdFrom"] = query.CreatedFrom?.ToString("O"),
            ["createdTo"] = query.CreatedTo?.ToString("O"),
            ["errorSearch"] = query.ErrorSearch,
            ["actionableOnly"] = query.ActionableOnly ? "true" : null,
            ["page"] = Math.Max(1, query.Page).ToString(),
            ["pageSize"] = Math.Clamp(query.PageSize, 1, 200).ToString(),
        };
        var queryString = string.Join('&', values.Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value!)}"));
        return GetJsonAsync<CollectionTaskPage>($"{CollectionPlatformPath}/tasks/search?{queryString}", token);
    }

    public Task<CollectionStatePage?> SearchCollectionStatesAsync(CollectionStateQuery query,
        CancellationToken token = default)
    {
        var values = new Dictionary<string, string?>
        {
            ["statuses"] = query.Statuses is null ? null : string.Join(',', query.Statuses),
            ["resourceType"] = query.ResourceType?.ToString(),
            ["provider"] = query.Provider,
            ["definitionId"] = query.DefinitionId,
            ["search"] = query.Search,
            ["page"] = Math.Max(1, query.Page).ToString(),
            ["pageSize"] = Math.Clamp(query.PageSize, 1, 200).ToString(),
        };
        var queryString = string.Join('&', values.Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value!)}"));
        return GetJsonAsync<CollectionStatePage>($"{CollectionPlatformPath}/states/search?{queryString}", token);
    }

    public Task<IReadOnlyList<PendingCollectionFailureNotification>?> GetCollectionFailureNotificationsAsync(
        int limit = 100, CancellationToken token = default)
        => GetJsonAsync<IReadOnlyList<PendingCollectionFailureNotification>>(
            $"{CollectionPlatformPath}/failure-notifications?limit={Math.Clamp(limit, 1, 1000)}", token);

    public Task<IReadOnlyList<CollectionFailureGroup>?> GetCollectionFailureGroupsAsync(
        int limit = 5000, CancellationToken token = default)
        => GetJsonAsync<IReadOnlyList<CollectionFailureGroup>>(
            $"{CollectionPlatformPath}/failure-notifications/groups?limit={Math.Clamp(limit, 1, 10000)}", token);

    public Task<AdminApiResult<CollectionFailureRecoveryResult>> RecoverCollectionFailuresAsync(
        RecoverCollectionFailuresRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<CollectionFailureRecoveryResult>(HttpMethod.Post,
            $"{CollectionPlatformPath}/failure-notifications/recover", request, token);

    public Task<AdminApiResult<BackfillBatchSnapshot>> CreateBackfillBatchAsync(
        CreateBackfillBatchRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<BackfillBatchSnapshot>(HttpMethod.Post,
            $"{CollectionPlatformPath}/backfills", request, token);

    public Task<AdminApiResult<RevisionImpactPreview>> PreviewRevisionImpactAsync(
        RevisionImpactPreviewRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<RevisionImpactPreview>(HttpMethod.Post,
            $"{CollectionPlatformPath}/revisions/preview", request, token);

    public Task<AdminApiResult<CollectionRevisionApplyResult>> ApplyRevisionAsync(
        ApplyCollectionRevisionRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<CollectionRevisionApplyResult>(HttpMethod.Post,
            $"{CollectionPlatformPath}/revisions/apply", request, token);

    public Task<AdminApiResult<RevisionRecollectionExpansion>> RecollectRevisionAsync(
        string definitionId, int revision, RevisionRecollectionRequest request,
        CancellationToken token = default)
        => SendCollectionPlatformAsync<RevisionRecollectionExpansion>(HttpMethod.Post,
            $"{CollectionPlatformPath}/revisions/{Uri.EscapeDataString(definitionId)}/{revision}/recollect",
            request, token);

    public Task<RevisionRecollectionProgress?> GetRevisionRecollectionProgressAsync(
        string definitionId, int revision, CancellationToken token = default)
        => GetJsonAsync<RevisionRecollectionProgress>(
            $"{CollectionPlatformPath}/revisions/{Uri.EscapeDataString(definitionId)}/{revision}/progress", token);

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

    public async Task<AdminApiResult<ExplicitUrlCollectionResult>> RequestCollectionByUrlAsync(
        string url, CancellationToken token = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{CollectionPlatformPath}/requests/by-url")
        { Content = JsonContent.Create(new CreateExplicitUrlCollectionRequest(url), options: JsonOptions) };
        using var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
        var value = await response.Content.ReadFromJsonAsync<ExplicitUrlCollectionResult>(JsonOptions, token)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return AdminApiResult<ExplicitUrlCollectionResult>.Fail(
                [value?.ErrorMessage ?? "URLから収集対象を識別できませんでした。"]);
        return value is null
            ? AdminApiResult<ExplicitUrlCollectionResult>.Fail(["応答の解析に失敗しました。"])
            : AdminApiResult<ExplicitUrlCollectionResult>.Ok(value);
    }

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
