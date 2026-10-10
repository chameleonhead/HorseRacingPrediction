using System.Net.Http.Json;
using System.Text.Json;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Api.Endpoints.Collection;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.Web.ApiBrowsing;

public sealed partial class AdminApiClient
{
    private const string CollectionPlatformPath = "/api/v2/admin/collection";

    public Task<CollectionProgressSnapshot?> GetCollectionProgressAsync(CancellationToken token = default)
        => GetCollectionJsonAsync<CollectionProgressSnapshot>($"{CollectionPlatformPath}/operations/progress", "Progress", token);

    public Task<CollectionTaskViewCounts?> GetCollectionTaskViewCountsAsync(CancellationToken token = default)
        => GetCollectionJsonAsync<CollectionTaskViewCounts>($"{CollectionPlatformPath}/operations/task-view-counts", "Counts", token);

    public Task<CollectionOperationsDashboard?> GetCollectionOperationsDashboardAsync(CancellationToken token = default)
        => GetCollectionJsonAsync<CollectionOperationsDashboard>($"{CollectionPlatformPath}/operations/dashboard", "Dashboard", token);

    public async Task<GetCollectionRuntimeStatusResponse?> GetCollectionRuntimeStatusAsync(CancellationToken token = default)
    {
        var runtime = await GetCollectionJsonAsync<CollectionRuntimeStatusDto>(
            $"{CollectionPlatformPath}/operations/runtime-status", "Runtime", token).ConfigureAwait(false);
        return runtime is null ? null : new(runtime);
    }

    public Task<CollectionPipelineState?> GetCollectionPipelineAsync(CancellationToken token = default)
        => GetCollectionJsonAsync<CollectionPipelineState>($"{CollectionPlatformPath}/pipeline-state", "Pipeline", token);

    public Task<CollectionStateSnapshot?> GetCollectionStateAsync(ResourceKey resource,
        CollectionDefinitionId definition, CancellationToken token = default)
        => GetCollectionJsonAsync<CollectionStateSnapshot>(
            $"{CollectionPlatformPath}/resources/{resource.Type}/{Uri.EscapeDataString(resource.Provider)}" +
            $"/{Uri.EscapeDataString(resource.Id)}/definitions/{Uri.EscapeDataString(definition.Value)}/state", "State", token);

    public Task<CollectionResourceDetail?> GetCollectionResourceDetailAsync(ResourceKey resource,
        CollectionDefinitionId definition, int requestHistoryPage = 1, int taskHistoryPage = 1,
        int attemptHistoryPage = 1, int historyPageSize = 25,
        CancellationToken token = default)
        => GetCollectionJsonAsync<CollectionResourceDetail>(
            $"{CollectionPlatformPath}/resources/{resource.Type}/{Uri.EscapeDataString(resource.Provider)}" +
            $"/{Uri.EscapeDataString(resource.Id)}/definitions/{Uri.EscapeDataString(definition.Value)}" +
            $"?requestHistoryPage={Math.Max(1, requestHistoryPage)}" +
            $"&taskHistoryPage={Math.Max(1, taskHistoryPage)}" +
            $"&attemptHistoryPage={Math.Max(1, attemptHistoryPage)}" +
            $"&historyPageSize={Math.Clamp(historyPageSize, 1, 100)}", "Resource", token);

    public Task<IReadOnlyList<BackfillBatchSnapshot>?> GetBackfillBatchesAsync(
        CancellationToken token = default)
        => GetCollectionJsonAsync<IReadOnlyList<BackfillBatchSnapshot>>($"{CollectionPlatformPath}/backfill-batches", "Batches", token);

    public Task<BackfillBatchSnapshot?> GetBackfillBatchAsync(string batchId, CancellationToken token = default)
        => GetCollectionJsonAsync<BackfillBatchSnapshot>($"{CollectionPlatformPath}/backfill-batches/{Uri.EscapeDataString(batchId)}", "Batch", token);

    public Task<AdminApiResult<BackfillHoleRecoveryResult>> RecoverBackfillHolesAsync(string batchId,
        CancellationToken token = default)
        => SendCollectionPlatformAsync<BackfillHoleRecoveryResult>(HttpMethod.Post,
            $"{CollectionPlatformPath}/backfill-batches/{Uri.EscapeDataString(batchId)}/recovery-batches", null, "Recovery", token);

    public Task<RaceEntryOwnerRepairPreview?> ListRaceEntryOwnerRepairCandidatesAsync(
        DateOnly date, CancellationToken token = default)
        => GetCollectionJsonAsync<RaceEntryOwnerRepairPreview>(
            $"{CollectionPlatformPath}/race-entry-owner-repair-candidates?date={date:yyyy-MM-dd}", "Preview", token);

    public Task<AdminApiResult<RaceEntryOwnerRepairReceipt>> CreateRaceEntryOwnerRepairBatchAsync(
        RaceEntryOwnerRepairRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<RaceEntryOwnerRepairReceipt>(HttpMethod.Post,
            $"{CollectionPlatformPath}/race-entry-owner-repair-batches",
            new CreateRaceEntryOwnerRepairBatchRequest(new(request.Date, request.RaceIds, request.BatchId)), "Repair", token);

    public async Task<IReadOnlyList<CollectionTaskSummary>?> GetCollectionTasksAsync(
        CollectionTaskStatus? status = null, int limit = 200, CancellationToken token = default)
    {
        var page = await GetCollectionJsonAsync<CollectionTaskPage>(
            $"{CollectionPlatformPath}/tasks?limit={Math.Clamp(limit, 1, 1000)}" +
            (status is null ? string.Empty : $"&status={status}"), "Page", token).ConfigureAwait(false);
        return page?.Items;
    }

    public Task<CollectionExecutionBatchDetail?> GetCollectionExecutionBatchAsync(Guid executionBatchId,
        CancellationToken token = default)
        => GetCollectionJsonAsync<CollectionExecutionBatchDetail>(
            $"{CollectionPlatformPath}/execution-batches/{executionBatchId:D}", "Batch", token);

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
        return GetCollectionJsonAsync<CollectionTaskPage>($"{CollectionPlatformPath}/tasks?{queryString}", "Page", token);
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
        return GetCollectionJsonAsync<CollectionStatePage>($"{CollectionPlatformPath}/states?{queryString}", "Page", token);
    }

    public Task<IReadOnlyList<PendingCollectionFailureNotification>?> GetCollectionFailureNotificationsAsync(
        int limit = 100, CancellationToken token = default)
        => GetCollectionJsonAsync<IReadOnlyList<PendingCollectionFailureNotification>>(
            $"{CollectionPlatformPath}/failure-notifications?view=Actionable&limit={Math.Clamp(limit, 1, 1000)}", "Notifications", token);

    public Task<IReadOnlyList<CollectionFailureGroup>?> GetCollectionFailureGroupsAsync(
        int limit = 5000, CancellationToken token = default)
        => GetCollectionJsonAsync<IReadOnlyList<CollectionFailureGroup>>(
            $"{CollectionPlatformPath}/failure-notification-groups?limit={Math.Clamp(limit, 1, 10000)}", "Groups", token);

    public Task<CollectionFailureGroupPage?> GetCollectionFailureGroupAsync(string groupKey,
        string? search = null, int page = 1, int pageSize = 50, CancellationToken token = default)
    {
        var query = $"page={Math.Max(1, page)}&pageSize={Math.Clamp(pageSize, 1, 100)}";
        if (!string.IsNullOrWhiteSpace(search))
            query += $"&search={Uri.EscapeDataString(search.Trim())}";
        return GetCollectionJsonAsync<CollectionFailureGroupPage>(
            $"{CollectionPlatformPath}/failure-notification-groups/{Uri.EscapeDataString(groupKey)}?{query}", "Page", token);
    }

    public Task<AdminApiResult<CollectionFailureRecoveryResult>> RecoverCollectionFailuresAsync(
        RecoverCollectionFailuresRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<CollectionFailureRecoveryResult>(HttpMethod.Post,
            $"{CollectionPlatformPath}/recovery-batches", new CreateCollectionRecoveryBatchRequest(
                new("NotificationIds", request.NotificationIds, RequestedRevision: request.RequestedRevision,
                    Lane: request.Lane, Priority: request.Priority)), "Recovery", token);

    public async Task<AdminApiResult<CollectionFailureRecoveryResult>> RecoverCollectionFailureGroupAsync(
        string groupKey, RecoverCollectionFailureGroupRequest request, CancellationToken token = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"{CollectionPlatformPath}/recovery-batches", new CreateCollectionRecoveryBatchRequest(
                new("GroupKey", GroupKey: groupKey, ExpectedNotificationIds: request.ExpectedNotificationIds,
                    RequestedRevision: request.RequestedRevision, Lane: request.Lane, Priority: request.Priority)),
            JsonOptions, token).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            return AdminApiResult<CollectionFailureRecoveryResult>.Fail(
                ["対象が更新されたため、画面を更新してもう一度確認してください。"]);
        if (!response.IsSuccessStatusCode)
            return AdminApiResult<CollectionFailureRecoveryResult>.Fail(
                await ReadErrorsAsync(response, token).ConfigureAwait(false));
        var value = await ReadCollectionResponseAsync<CollectionFailureRecoveryResult>(
            response, "Recovery", token).ConfigureAwait(false);
        return value is null
            ? AdminApiResult<CollectionFailureRecoveryResult>.Fail(["応答の解析に失敗しました。"])
            : AdminApiResult<CollectionFailureRecoveryResult>.Ok(value);
    }

    public Task<AdminApiResult<CreateBackfillBatchResponse>> CreateBackfillBatchAsync(
        CreateBackfillBatchRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<CreateBackfillBatchResponse>(HttpMethod.Post,
            $"{CollectionPlatformPath}/backfill-batches", request, token);

    public Task<AdminApiResult<RevisionImpactPreview>> PreviewRevisionImpactAsync(
        RevisionImpactPreviewRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<RevisionImpactPreview>(HttpMethod.Post,
            $"{CollectionPlatformPath}/revision-impact-previews", new PreviewRevisionImpactRequest(
                new PreviewRevisionImpactInputDto(request.DefinitionId, request.Revision,
                    new RevisionImpactInputDto(request.Impact.ScopeType,
                        request.Impact.Resources?.Select(x => new CollectionResourceKeyDto(x.Type, x.Provider, x.Id)).ToArray(),
                        request.Impact.From, request.Impact.To, request.Impact.NamedCondition))), "Preview", token);

    public Task<AdminApiResult<CollectionRevisionApplyResult>> ApplyRevisionAsync(
        ApplyCollectionRevisionRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<CollectionRevisionApplyResult>(HttpMethod.Post,
            $"{CollectionPlatformPath}/definitions/{Uri.EscapeDataString(request.DefinitionId)}/revisions",
            new CreateCollectionRevisionRequest(new CreateCollectionRevisionInputDto(request.Revision,
                request.Description, new RevisionImpactInputDto(request.Impact.ScopeType,
                    request.Impact.Resources?.Select(x => new CollectionResourceKeyDto(x.Type, x.Provider, x.Id)).ToArray(),
                    request.Impact.From, request.Impact.To, request.Impact.NamedCondition))), "Result", token);

    public Task<AdminApiResult<CollectionRecollectionBatchResponse>> RecollectRevisionAsync(
        string definitionId, int revision, RevisionRecollectionRequest request,
        CancellationToken token = default)
        => SendCollectionPlatformAsync<CollectionRecollectionBatchResponse>(HttpMethod.Post,
            $"{CollectionPlatformPath}/recollection-batches",
            new CreateRecollectionBatchRequest(new("Revision", definitionId, revision,
                Lane: request.Lane, Priority: request.Priority)), "Batch", token);

    public Task<RevisionRecollectionProgress?> GetRevisionRecollectionProgressAsync(
        string definitionId, int revision, CancellationToken token = default)
        => GetCollectionJsonAsync<RevisionRecollectionProgress>(
            $"{CollectionPlatformPath}/recollection-batches?definition={Uri.EscapeDataString(definitionId)}&revision={revision}", "Progress", token);

    public Task<AdminApiResult> PauseCollectionPipelineAsync(string? reason,
        CancellationToken token = default)
        => SendAsync(HttpMethod.Put, $"{CollectionPlatformPath}/pipeline",
            new SetCollectionPipelineRequest(new SetCollectionPipelineInputDto(true, reason)), token);

    public Task<AdminApiResult> ResumeCollectionPipelineAsync(CancellationToken token = default)
        => SendAsync(HttpMethod.Put, $"{CollectionPlatformPath}/pipeline",
            new SetCollectionPipelineRequest(new SetCollectionPipelineInputDto(false)), token);

    public Task<AdminApiResult> CancelCollectionTaskAsync(Guid taskId, CancellationToken token = default)
        => SendAsync(HttpMethod.Patch, $"{CollectionPlatformPath}/tasks/{taskId:D}",
            new CancelCollectionTaskRequest(new CancelCollectionTaskInputDto(true)), token);

    public Task<AdminApiResult<CollectionTaskSubmissionResponse>> RequestCollectionAsync(
        CreateCollectionRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<CollectionTaskSubmissionResponse>(HttpMethod.Post,
            $"{CollectionPlatformPath}/tasks", new CreateCollectionTaskRequest(new CreateCollectionTaskInputDto("Resource",
                new CollectionResourceTaskInputDto(request.ResourceType, request.Provider, request.ResourceId,
                    request.DefinitionId, request.RequestedRevision, request.Reason, request.Lane, request.Priority,
                    request.ExplicitUrl, request.BatchId, request.EffectiveDate, request.Attributes))), "Submission", token);

    public async Task<AdminApiResult<ExplicitUrlCollectionResult>> RequestCollectionByUrlAsync(
        string url, CancellationToken token = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{CollectionPlatformPath}/tasks")
        {
            Content = JsonContent.Create(new CreateCollectionTaskRequest(new CreateCollectionTaskInputDto(
            "SourceUrl", SourceUrl: new(url))), options: JsonOptions)
        };
        using var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            var submission = await ReadCollectionResponseAsync<CollectionTaskSubmissionResponse>(response,
                "Submission", token).ConfigureAwait(false);
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
            $"{CollectionPlatformPath}/task-batch-previews", new PreviewCollectionTaskBatchRequest(
                new PreviewCollectionTaskBatchInputDto(request.DefinitionId, request.RequestedRevision, request.Reason,
                    request.Selection, request.Provider,
                    request.Resources?.Select(x => new CollectionResourceKeyDto(x.Type, x.Provider, x.Id)).ToArray(),
                    request.From, request.To, request.TrainerId, request.LastCollectedBefore,
                    request.ImpactRevision,
                    request.ExpectedResources?.Select(x => new CollectionResourceKeyDto(x.Type, x.Provider, x.Id)).ToArray(),
                    request.BatchId, request.Lane, request.Priority)), "Preview", token);

    public Task<AdminApiResult<CollectionTaskBatchSubmissionResponse>> ExecuteBulkCollectionAsync(
        BulkCollectionOperationRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<CollectionTaskBatchSubmissionResponse>(HttpMethod.Post,
            $"{CollectionPlatformPath}/task-batches", new CreateCollectionTaskBatchRequest(
                new CreateCollectionTaskBatchInputDto("PreviewSelection",
                    new CollectionSelectionTaskBatchInputDto(request.DefinitionId, request.RequestedRevision,
                        request.Reason, request.Selection.ToString(), request.Provider,
                        request.Resources?.Select(x => new CollectionResourceKeyDto(x.Type, x.Provider, x.Id)).ToArray(),
                        request.From, request.To, request.TrainerId, request.LastCollectedBefore,
                        request.ExpectedResources?.Select(x => new CollectionResourceKeyDto(x.Type, x.Provider, x.Id)).ToArray(),
                        request.BatchId, request.Lane, request.Priority))), "Submission", token);

    public Task<AdminApiResult<CollectionTaskBatchSubmissionResponse>> SubmitCollectionTaskBatchAsync(
        CollectionRequestBulkRequest request, CancellationToken token = default)
        => SendCollectionPlatformAsync<CollectionTaskBatchSubmissionResponse>(HttpMethod.Post,
            $"{CollectionPlatformPath}/task-batches", new CreateCollectionTaskBatchRequest(
                new CreateCollectionTaskBatchInputDto("ExplicitItems", ExplicitItems: request)), "Submission", token);

    public async Task<AdminApiResult<RacePeriodRecollectionPreview>> PreviewRacePeriodRecollectionAsync(
        CreateRacePeriodRecollectionRequest request, CancellationToken token = default)
    {
        var result = await SendCollectionPlatformAsync<PreviewRacePeriodRecollectionResponse>(HttpMethod.Post,
            $"{CollectionPlatformPath}/recollection-previews",
            new PreviewRacePeriodRecollectionRequest(new(request.From, request.To, request.Provider, request.BatchId)),
            token).ConfigureAwait(false);
        return result.Success
            ? AdminApiResult<RacePeriodRecollectionPreview>.Ok(new(result.Value!.Preview.From,
                result.Value.Preview.To, result.Value.Preview.InclusiveDays, result.Value.Preview.Provider))
            : AdminApiResult<RacePeriodRecollectionPreview>.Fail(result.Errors);
    }

    public async Task<AdminApiResult<RacePeriodRecollectionReceipt>> CreateRacePeriodRecollectionAsync(
        CreateRacePeriodRecollectionRequest request, CancellationToken token = default)
    {
        var result = await SendCollectionPlatformAsync<CollectionRecollectionBatchResponse>(HttpMethod.Post,
            $"{CollectionPlatformPath}/recollection-batches", new CreateRecollectionBatchRequest(
                new("RacePeriod", Provider: request.Provider, From: request.From, To: request.To,
                    BatchId: request.BatchId)), "Batch", token)
            .ConfigureAwait(false);
        if (!result.Success) return AdminApiResult<RacePeriodRecollectionReceipt>.Fail(result.Errors);
        return result.Value?.RacePeriod is { } receipt
            ? AdminApiResult<RacePeriodRecollectionReceipt>.Ok(receipt)
            : AdminApiResult<RacePeriodRecollectionReceipt>.Fail(["再取得の応答を解析できませんでした。"]);
    }

    public Task<AdminApiResult<RaceEntryOwnerMigrationProgress>> PreviewRaceEntryOwnerMigrationAsync(
        CancellationToken token = default)
        => SendCollectionPlatformAsync<RaceEntryOwnerMigrationProgress>(HttpMethod.Post,
            $"{CollectionPlatformPath}/migration-previews/race-entry-owner-repair", null, "Preview", token);

    public Task<AdminApiResult<RaceEntryOwnerMigrationProgress>> ApplyRaceEntryOwnerMigrationAsync(
        CancellationToken token = default)
        => SendCollectionPlatformAsync<RaceEntryOwnerMigrationProgress>(HttpMethod.Post,
            $"{CollectionPlatformPath}/migrations/race-entry-owner-repair", null, "Progress", token);

    public Task<RaceEntryOwnerMigrationProgress?> GetRaceEntryOwnerMigrationProgressAsync(
        CancellationToken token = default)
        => GetCollectionJsonAsync<RaceEntryOwnerMigrationProgress>(
            $"{CollectionPlatformPath}/migrations/race-entry-owner-repair", "Progress", token);

    private Task<AdminApiResult<T>> SendCollectionPlatformAsync<T>(HttpMethod method, string path,
        object? body, CancellationToken token)
        => SendCollectionPlatformAsync<T>(method, path, body, responseProperty: null, token);

    private async Task<AdminApiResult<T>> SendCollectionPlatformAsync<T>(HttpMethod method, string path,
        object? body, string? responseProperty, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return AdminApiResult<T>.Fail(await ReadErrorsAsync(response, token).ConfigureAwait(false));
        var value = responseProperty is null
            ? await response.Content.ReadFromJsonAsync<T>(JsonOptions, token).ConfigureAwait(false)
            : await ReadCollectionResponseAsync<T>(response, responseProperty, token).ConfigureAwait(false);
        return value is null
            ? AdminApiResult<T>.Fail(["応答の解析に失敗しました。"])
            : AdminApiResult<T>.Ok(value);
    }

    private async Task<T?> GetCollectionJsonAsync<T>(string path, string responseProperty,
        CancellationToken token)
    {
        using var response = await _httpClient.GetAsync(path, token).ConfigureAwait(false);
        if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.NoContent)
            return default;
        response.EnsureSuccessStatusCode();
        return await ReadCollectionResponseAsync<T>(response, responseProperty, token).ConfigureAwait(false);
    }

    private static async Task<T?> ReadCollectionResponseAsync<T>(HttpResponseMessage response,
        string responseProperty, CancellationToken token)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
        var root = document.RootElement;
        JsonElement value = default;
        var found = root.ValueKind == JsonValueKind.Object
            && root.EnumerateObject().Any(property =>
            {
                if (!string.Equals(property.Name, responseProperty, StringComparison.OrdinalIgnoreCase)) return false;
                value = property.Value;
                return true;
            });
        return found ? value.Deserialize<T>(JsonOptions) : default;
    }
}
