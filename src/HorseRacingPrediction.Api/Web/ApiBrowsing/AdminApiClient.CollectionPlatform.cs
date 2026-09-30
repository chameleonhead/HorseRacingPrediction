using System.Net.Http.Json;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Api.Endpoints.Collection;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.Web.ApiBrowsing;

public sealed partial class AdminApiClient
{
    private const string CollectionPlatformPath = "/api/v2/admin/collection";

    public Task<CollectionProgressSnapshot?> GetCollectionProgressAsync(CancellationToken token = default)
        => GetJsonAsync<CollectionProgressSnapshot>($"{CollectionPlatformPath}/operations/progress", token);

    public Task<CollectionTaskViewCounts?> GetCollectionTaskViewCountsAsync(CancellationToken token = default)
        => GetJsonAsync<CollectionTaskViewCounts>($"{CollectionPlatformPath}/operations/task-view-counts", token);

    public Task<CollectionOperationsDashboard?> GetCollectionOperationsDashboardAsync(CancellationToken token = default)
        => GetJsonAsync<CollectionOperationsDashboard>($"{CollectionPlatformPath}/operations/dashboard", token);

    public Task<CollectionPipelineState?> GetCollectionPipelineAsync(CancellationToken token = default)
        => GetJsonAsync<CollectionPipelineState>($"{CollectionPlatformPath}/pipeline-state", token);

    public Task<CollectionStateSnapshot?> GetCollectionStateAsync(ResourceKey resource,
        CollectionDefinitionId definition, CancellationToken token = default)
        => GetJsonAsync<CollectionStateSnapshot>(
            $"{CollectionPlatformPath}/resources/{resource.Type}/{Uri.EscapeDataString(resource.Provider)}" +
            $"/{Uri.EscapeDataString(resource.Id)}/definitions/{Uri.EscapeDataString(definition.Value)}/state", token);

    public Task<CollectionResourceDetail?> GetCollectionResourceDetailAsync(ResourceKey resource,
        CollectionDefinitionId definition, int requestHistoryPage = 1, int taskHistoryPage = 1,
        int attemptHistoryPage = 1, int historyPageSize = 25,
        CancellationToken token = default)
        => GetJsonAsync<CollectionResourceDetail>(
            $"{CollectionPlatformPath}/resources/{resource.Type}/{Uri.EscapeDataString(resource.Provider)}" +
            $"/{Uri.EscapeDataString(resource.Id)}/definitions/{Uri.EscapeDataString(definition.Value)}" +
            $"?requestHistoryPage={Math.Max(1, requestHistoryPage)}" +
            $"&taskHistoryPage={Math.Max(1, taskHistoryPage)}" +
            $"&attemptHistoryPage={Math.Max(1, attemptHistoryPage)}" +
            $"&historyPageSize={Math.Clamp(historyPageSize, 1, 100)}", token);

    public Task<IReadOnlyList<BackfillBatchSnapshot>?> GetBackfillBatchesAsync(
        CancellationToken token = default)
        => GetJsonAsync<IReadOnlyList<BackfillBatchSnapshot>>($"{CollectionPlatformPath}/backfill-batches", token);

    public Task<BackfillBatchSnapshot?> GetBackfillBatchAsync(string batchId, CancellationToken token = default)
        => GetJsonAsync<BackfillBatchSnapshot>($"{CollectionPlatformPath}/backfill-batches/{Uri.EscapeDataString(batchId)}", token);

    public Task<AdminApiResult<BackfillHoleRecoveryResult>> RecoverBackfillHolesAsync(string batchId,
        CancellationToken token = default)
        => SendCollectionPlatformAsync<BackfillHoleRecoveryResult>(HttpMethod.Post,
            $"{CollectionPlatformPath}/backfill-batches/{Uri.EscapeDataString(batchId)}/recovery-batches", new { }, token);

    public Task<RaceEntryOwnerRepairPreview?> ListRaceEntryOwnerRepairCandidatesAsync(
        DateOnly date, CancellationToken token = default)
        => GetJsonAsync<RaceEntryOwnerRepairPreview>(
            $"{CollectionPlatformPath}/race-entry-owner-repair-candidates?date={date:yyyy-MM-dd}", token);

    public Task<AdminApiResult<RaceEntryOwnerRepairReceipt>> CreateRaceEntryOwnerRepairBatchAsync(
        RaceEntryOwnerRepairRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<RaceEntryOwnerRepairReceipt>(HttpMethod.Post,
            $"{CollectionPlatformPath}/race-entry-owner-repair-batches", request, token);

    public async Task<IReadOnlyList<CollectionTaskSummary>?> GetCollectionTasksAsync(
        CollectionTaskStatus? status = null, int limit = 200, CancellationToken token = default)
    {
        var page = await GetJsonAsync<CollectionTaskPage>(
            $"{CollectionPlatformPath}/tasks?limit={Math.Clamp(limit, 1, 1000)}" +
            (status is null ? string.Empty : $"&status={status}"), token).ConfigureAwait(false);
        return page?.Items;
    }

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
            ["latestOnly"] = query.LatestOnly ? "true" : null,
            ["page"] = Math.Max(1, query.Page).ToString(),
            ["pageSize"] = Math.Clamp(query.PageSize, 1, 200).ToString(),
        };
        var queryString = string.Join('&', values.Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value!)}"));
        return GetJsonAsync<CollectionTaskPage>($"{CollectionPlatformPath}/tasks?{queryString}", token);
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
        return GetJsonAsync<CollectionStatePage>($"{CollectionPlatformPath}/states?{queryString}", token);
    }

    public Task<IReadOnlyList<PendingCollectionFailureNotification>?> GetCollectionFailureNotificationsAsync(
        int limit = 100, CancellationToken token = default)
        => GetJsonAsync<IReadOnlyList<PendingCollectionFailureNotification>>(
            $"{CollectionPlatformPath}/failure-notifications?view=Actionable&limit={Math.Clamp(limit, 1, 1000)}", token);

    public Task<IReadOnlyList<CollectionFailureGroup>?> GetCollectionFailureGroupsAsync(
        int limit = 5000, CancellationToken token = default)
        => GetJsonAsync<IReadOnlyList<CollectionFailureGroup>>(
            $"{CollectionPlatformPath}/failure-notification-groups?limit={Math.Clamp(limit, 1, 10000)}", token);

    public Task<CollectionFailureGroupPage?> GetCollectionFailureGroupAsync(string groupKey,
        string? search = null, int page = 1, int pageSize = 50, CancellationToken token = default)
    {
        var query = $"page={Math.Max(1, page)}&pageSize={Math.Clamp(pageSize, 1, 100)}";
        if (!string.IsNullOrWhiteSpace(search))
            query += $"&search={Uri.EscapeDataString(search.Trim())}";
        return GetJsonAsync<CollectionFailureGroupPage>(
            $"{CollectionPlatformPath}/failure-notification-groups/{Uri.EscapeDataString(groupKey)}?{query}", token);
    }

    public Task<AdminApiResult<CollectionFailureRecoveryResult>> RecoverCollectionFailuresAsync(
        RecoverCollectionFailuresRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<CollectionFailureRecoveryResult>(HttpMethod.Post,
            $"{CollectionPlatformPath}/recovery-batches", new CollectionRecoveryBatchRequest("NotificationIds",
                request.NotificationIds, RequestedRevision: request.RequestedRevision, Lane: request.Lane, Priority: request.Priority), token);

    public Task<AdminApiResult<CollectionFailureRecoveryResult>> RecoverCollectionFailureGroupAsync(
        string groupKey, RecoverCollectionFailureGroupRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<CollectionFailureRecoveryResult>(HttpMethod.Post,
            $"{CollectionPlatformPath}/recovery-batches", new CollectionRecoveryBatchRequest("GroupKey",
                GroupKey: groupKey, ExpectedNotificationIds: request.ExpectedNotificationIds,
                RequestedRevision: request.RequestedRevision, Lane: request.Lane, Priority: request.Priority), token);

    public Task<AdminApiResult<BackfillBatchSnapshot>> CreateBackfillBatchAsync(
        CreateBackfillBatchRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<BackfillBatchSnapshot>(HttpMethod.Post,
            $"{CollectionPlatformPath}/backfill-batches", request, token);

    public Task<AdminApiResult<RevisionImpactPreview>> PreviewRevisionImpactAsync(
        RevisionImpactPreviewRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<RevisionImpactPreview>(HttpMethod.Post,
            $"{CollectionPlatformPath}/revision-impact-previews", request, token);

    public Task<AdminApiResult<CollectionRevisionApplyResult>> ApplyRevisionAsync(
        ApplyCollectionRevisionRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<CollectionRevisionApplyResult>(HttpMethod.Post,
            $"{CollectionPlatformPath}/definitions/{Uri.EscapeDataString(request.DefinitionId)}/revisions",
            new CreateCollectionRevisionRequest(request.Revision, request.Description, request.Impact), token);

    public Task<AdminApiResult<CollectionRecollectionBatchResponse>> RecollectRevisionAsync(
        string definitionId, int revision, RevisionRecollectionRequest request,
        CancellationToken token = default)
        => SendCollectionPlatformAsync<CollectionRecollectionBatchResponse>(HttpMethod.Post,
            $"{CollectionPlatformPath}/recollection-batches",
            new CollectionRecollectionBatchRequest("Revision", definitionId, revision, Lane: request.Lane, Priority: request.Priority), token);

    public Task<RevisionRecollectionProgress?> GetRevisionRecollectionProgressAsync(
        string definitionId, int revision, CancellationToken token = default)
        => GetJsonAsync<RevisionRecollectionProgress>(
            $"{CollectionPlatformPath}/recollection-batches?definition={Uri.EscapeDataString(definitionId)}&revision={revision}", token);

    public Task<AdminApiResult> PauseCollectionPipelineAsync(string? reason,
        CancellationToken token = default)
        => SendAsync(HttpMethod.Put, $"{CollectionPlatformPath}/pipeline",
            new SetCollectionPipelineRequest(true, reason), token);

    public Task<AdminApiResult> ResumeCollectionPipelineAsync(CancellationToken token = default)
        => SendAsync(HttpMethod.Put, $"{CollectionPlatformPath}/pipeline", new SetCollectionPipelineRequest(false), token);

    public Task<AdminApiResult> CancelCollectionTaskAsync(Guid taskId, CancellationToken token = default)
        => SendAsync(HttpMethod.Patch, $"{CollectionPlatformPath}/tasks/{taskId:D}", new CollectionTaskCancellationRequest(true), token);

    public Task<AdminApiResult<CollectionTaskSubmissionResponse>> RequestCollectionAsync(
        CreateCollectionRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<CollectionTaskSubmissionResponse>(HttpMethod.Post,
            $"{CollectionPlatformPath}/tasks", new CollectionTaskRequest("Resource", new CollectionResourceTaskRequest(
                request.ResourceType, request.Provider, request.ResourceId, request.DefinitionId, request.RequestedRevision,
                request.Reason, request.Lane, request.Priority, request.ExplicitUrl, request.BatchId,
                request.EffectiveDate, request.Attributes)), token);

    public async Task<AdminApiResult<ExplicitUrlCollectionResult>> RequestCollectionByUrlAsync(
        string url, CancellationToken token = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{CollectionPlatformPath}/tasks")
        { Content = JsonContent.Create(new CollectionTaskRequest("SourceUrl", SourceUrl: new(url)), options: JsonOptions) };
        using var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            var submission = await response.Content.ReadFromJsonAsync<CollectionTaskSubmissionResponse>(JsonOptions, token).ConfigureAwait(false);
            if (submission is null) return AdminApiResult<ExplicitUrlCollectionResult>.Fail(["応答の解析に失敗しました。"]);
            return AdminApiResult<ExplicitUrlCollectionResult>.Ok(new(true, submission.Resource, submission.Definition,
                submission.EffectiveDate, submission.Attributes, submission.ExplicitUrl, null, null, submission.Receipt));
        }
        var value = await response.Content.ReadFromJsonAsync<ExplicitUrlCollectionResult>(JsonOptions, token).ConfigureAwait(false);
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
            $"{CollectionPlatformPath}/task-batch-previews", request, token);

    public Task<AdminApiResult<CollectionTaskBatchSubmissionResponse>> ExecuteBulkCollectionAsync(
        BulkCollectionOperationRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<CollectionTaskBatchSubmissionResponse>(HttpMethod.Post,
            $"{CollectionPlatformPath}/task-batches", new CollectionTaskBatchRequest("PreviewSelection",
                new CollectionSelectionTaskBatchRequest(request.DefinitionId, request.RequestedRevision, request.Reason,
                    request.Selection.ToString(), request.Provider, request.Resources, request.From, request.To,
                    request.TrainerId, request.LastCollectedBefore, request.ExpectedResources, request.BatchId,
                    request.Lane, request.Priority)), token);

    public Task<AdminApiResult<CollectionTaskBatchSubmissionResponse>> SubmitCollectionTaskBatchAsync(
        CollectionRequestBulkRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<CollectionTaskBatchSubmissionResponse>(HttpMethod.Post,
            $"{CollectionPlatformPath}/task-batches", new CollectionTaskBatchRequest("ExplicitItems",
                ExplicitItems: request), token);

    public Task<AdminApiResult<RacePeriodRecollectionPreview>> PreviewRacePeriodRecollectionAsync(
        CreateRacePeriodRecollectionRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<RacePeriodRecollectionPreview>(HttpMethod.Post,
            $"{CollectionPlatformPath}/recollection-previews", request, token);

    public async Task<AdminApiResult<RacePeriodRecollectionReceipt>> CreateRacePeriodRecollectionAsync(
        CreateRacePeriodRecollectionRequest request, CancellationToken token = default)
    {
        var result = await SendCollectionPlatformAsync<CollectionRecollectionBatchResponse>(HttpMethod.Post,
            $"{CollectionPlatformPath}/recollection-batches", new CollectionRecollectionBatchRequest("RacePeriod",
                Provider: request.Provider, From: request.From, To: request.To, BatchId: request.BatchId), token)
            .ConfigureAwait(false);
        if (!result.Success) return AdminApiResult<RacePeriodRecollectionReceipt>.Fail(result.Errors);
        return result.Value?.RacePeriod is { } receipt
            ? AdminApiResult<RacePeriodRecollectionReceipt>.Ok(receipt)
            : AdminApiResult<RacePeriodRecollectionReceipt>.Fail(["再取得の応答を解析できませんでした。"]);
    }

    public Task<AdminApiResult<RaceEntryOwnerMigrationProgress>> PreviewRaceEntryOwnerMigrationAsync(
        CancellationToken token = default)
        => SendCollectionPlatformAsync<RaceEntryOwnerMigrationProgress>(HttpMethod.Post,
            $"{CollectionPlatformPath}/migration-previews/race-entry-owner-repair", new { }, token);

    public Task<AdminApiResult<RaceEntryOwnerMigrationProgress>> ApplyRaceEntryOwnerMigrationAsync(
        CancellationToken token = default)
        => SendCollectionPlatformAsync<RaceEntryOwnerMigrationProgress>(HttpMethod.Post,
            $"{CollectionPlatformPath}/migrations/race-entry-owner-repair", new { }, token);

    public Task<RaceEntryOwnerMigrationProgress?> GetRaceEntryOwnerMigrationProgressAsync(
        CancellationToken token = default)
        => GetJsonAsync<RaceEntryOwnerMigrationProgress>(
            $"{CollectionPlatformPath}/migrations/race-entry-owner-repair", token);

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
