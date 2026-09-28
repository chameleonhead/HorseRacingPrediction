using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.Http;
using HorseRacingPrediction.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Collector.CollectionPlatform;

public sealed class CollectionPlatformWorkerClient
{
    private static readonly JsonSerializerOptions AcquireResponseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    private readonly HttpClient _client;
    private readonly CollectionDefinitionHandlerRegistry _handlers;
    private readonly ILogger<CollectionPlatformWorkerClient> _logger;
    private readonly ICollectionTaskTelemetryClock _clock;

    public CollectionPlatformWorkerClient(HttpClient client, CollectionDefinitionHandlerRegistry handlers,
        ILogger<CollectionPlatformWorkerClient>? logger = null)
        : this(client, handlers, logger, SystemCollectionTaskTelemetryClock.Instance)
    {
    }

    internal CollectionPlatformWorkerClient(HttpClient client, CollectionDefinitionHandlerRegistry handlers,
        ILogger<CollectionPlatformWorkerClient>? logger, ICollectionTaskTelemetryClock clock)
    {
        _client = client;
        _handlers = handlers;
        _logger = logger ?? NullLogger<CollectionPlatformWorkerClient>.Instance;
        _clock = clock;
    }

    public async Task<CollectionExecutionAcquireResult> AcquireNextAsync(CollectionWakeSignal wake,
        string queueMessageId, CancellationToken cancellationToken)
    {
        using var response = await _client.PostAsJsonAsync("api/v2/internal/collection/execution-leases",
            new CollectionExecutionAcquireRequest(wake, queueMessageId), cancellationToken).ConfigureAwait(false);
        var expectedStatus = response.StatusCode;
        if (expectedStatus is not (HttpStatusCode.Created or HttpStatusCode.OK))
        {
            response.EnsureSuccessStatusCode();
            throw new InvalidDataException($"Unexpected collection execution acquire status {(int)expectedStatus}.");
        }

        var result = await response.Content.ReadFromJsonAsync<CollectionExecutionAcquireResult>(AcquireResponseJsonOptions,
            cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException("Collection execution acquire response was empty.");
        if (expectedStatus == HttpStatusCode.Created)
        {
            if (result.Status != CollectionExecutionAcquireStatus.Acquired
                || result.ExecutionBatchId is not Guid executionBatchId || executionBatchId == Guid.Empty
                || string.IsNullOrWhiteSpace(result.LeaseToken) || result.Envelope is null
                || result.Envelope.EnvelopeId != wake.DispatchEnvelopeId || result.Envelope.Tasks is null
                || !result.Envelope.IsSupported()
                || result.NoWorkReason is not null || result.ReservationReleaseOutcome is not null)
                throw new InvalidDataException("Collection execution acquire response did not contain a valid acquired lease.");
        }
        else if (result.Status != CollectionExecutionAcquireStatus.NoWork
            || result.NoWorkReason is null || !Enum.IsDefined(result.NoWorkReason.Value)
            || result.ExecutionBatchId is not null || result.LeaseToken is not null || result.Envelope is not null
            || result.StartBefore is not null
            || (result.SafeToReleaseReservation && result.ReservationReleaseOutcome is null or CollectionReservationReleaseOutcome.NotAttempted)
            || (!result.SafeToReleaseReservation && result.ReservationReleaseOutcome is not null))
        {
            throw new InvalidDataException("Collection execution acquire response did not contain a valid typed NoWork result.");
        }
        return result;
    }

    public async Task StartExecutionAsync(Guid executionBatchId, string leaseToken, string? lambdaRequestId,
        CancellationToken cancellationToken)
    {
        using var response = await _client.PatchAsJsonAsync(
            $"api/v2/internal/collection/execution-batches/{executionBatchId:D}",
            new { Transition = "Start", LeaseToken = leaseToken, LeaseSeconds = 960, LambdaRequestId = lambdaRequestId }, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task CompleteExecutionAsync(Guid executionBatchId, string leaseToken,
        CancellationToken cancellationToken)
    {
        using var response = await _client.PatchAsJsonAsync(
            $"api/v2/internal/collection/execution-batches/{executionBatchId:D}",
            new { Transition = "Complete", LeaseToken = leaseToken }, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task ExecuteAsync(CollectionTaskNotification notification, CancellationToken cancellationToken)
    {
        var totalStarted = _clock.GetTimestamp();
        var acquireStarted = _clock.GetTimestamp();
        using var acquireResponse = await _client.PostAsJsonAsync(
            $"api/v2/internal/collection/tasks/{notification.TaskId}/leases",
            new
            {
                notification.DispatchGeneration,
                LeaseSeconds = 900,
                Correlation = CollectionAttemptCorrelationScope.Current
            }, cancellationToken).ConfigureAwait(false);
        acquireResponse.EnsureSuccessStatusCode();
        var acquire = await acquireResponse.Content.ReadFromJsonAsync<CollectionTaskAcquireResult>(cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Collection task acquire response was empty.");
        var acquireElapsed = _clock.GetElapsedTime(acquireStarted, _clock.GetTimestamp());
        if (acquire.Status is CollectionTaskAcquireStatus.AlreadyTerminal
            or CollectionTaskAcquireStatus.SupersededGeneration or CollectionTaskAcquireStatus.RepairHeld) return;
        if (acquire.Status == CollectionTaskAcquireStatus.ActiveElsewhere)
            throw new CollectionTaskActiveElsewhereException(notification.TaskId);
        var task = acquire.Task ?? throw new InvalidOperationException("Acquired task lease was empty.");

        CollectionAttemptCompletion? completion = null;
        var handlerElapsed = TimeSpan.Zero;
        var handlerMeasured = false;
        var completeElapsed = TimeSpan.Zero;
        var handlerApiTiming = CollectionRuntimeTimingContext.BeginTask();
        var handlerApi = default(CollectionRuntimeTimingContext.TimingSnapshot);
        try
        {
            var expected = JraSessionExecutionScope.CurrentCompatibilityKey;
            if (expected is not null && !IsCompatible(task, expected))
                throw new CollectionDispatchCompatibilityException(notification.TaskId);

            var handlerStarted = _clock.GetTimestamp();
            try
            {
                using var leaseScope = CollectionWorkerLeaseContext.Push(task.TaskId, task.LeaseToken,
                    task.RaceHoldGeneration, task.EntryAssignmentFingerprint);
                completion = await _handlers.Resolve(task.Definition, task.Resource.Type)
                    .CollectAsync(task, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                completion = CollectionAttemptFailureClassifier.WithTaskContext(
                    new(CollectionAttemptResult.TransientFailure, "CollectorTimeout",
                        "Collector execution was cancelled or reached its deadline.",
                        RetryAt: HorseRacingPrediction.Contracts.Time.JstTime.Now().AddMinutes(1)),
                    task);
                handlerElapsed = _clock.GetElapsedTime(handlerStarted, _clock.GetTimestamp());
                handlerMeasured = true;
                handlerApiTiming.Dispose();
                handlerApi = handlerApiTiming.Accumulator.Snapshot();
                var cancelledCompleteStarted = _clock.GetTimestamp();
                using var report = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await CompleteAsync(notification.TaskId, task.LeaseToken,
                    completion, report.Token).ConfigureAwait(false);
                completeElapsed = _clock.GetElapsedTime(cancelledCompleteStarted, _clock.GetTimestamp());
                throw;
            }
            catch (Exception ex)
            {
                completion = CollectionAttemptFailureClassifier.FromException(ex);
            }
            finally
            {
                if (!handlerMeasured)
                    handlerElapsed = _clock.GetElapsedTime(handlerStarted, _clock.GetTimestamp());
                handlerApiTiming.Dispose();
                handlerApi = handlerApiTiming.Accumulator.Snapshot();
            }

            completion = CollectionAttemptFailureClassifier.WithTaskContext(completion!, task);
            var completeStarted = _clock.GetTimestamp();
            await CompleteAsync(notification.TaskId, task.LeaseToken, completion, cancellationToken).ConfigureAwait(false);
            completeElapsed = _clock.GetElapsedTime(completeStarted, _clock.GetTimestamp());
        }
        finally
        {
            handlerApiTiming.Dispose();
            handlerApi = handlerApiTiming.Accumulator.Snapshot();
            var totalElapsed = _clock.GetElapsedTime(totalStarted, _clock.GetTimestamp());
            var attributed = acquireElapsed + handlerElapsed + completeElapsed;
            var unattributed = totalElapsed > attributed ? totalElapsed - attributed : TimeSpan.Zero;
            var result = completion?.Result.ToString() ?? "UnexpectedFailure";
            var handlerNonApiElapsed = handlerElapsed > handlerApi.ApiElapsed
                ? handlerElapsed - handlerApi.ApiElapsed
                : TimeSpan.Zero;
            var memorySize = int.TryParse(Environment.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_MEMORY_SIZE"),
                out var configuredMemory) ? configuredMemory : 0;
            _logger.LogInformation(
                "Collection task runtime. Definition={Definition} ResourceType={ResourceType} Result={Result} MemorySizeMiB={MemorySizeMiB} TotalMs={TotalMs} AcquireMs={AcquireMs} HandlerMs={HandlerMs} HandlerApiMs={HandlerApiMs} HandlerApiCallCount={HandlerApiCallCount} HandlerNonApiMs={HandlerNonApiMs} CompleteMs={CompleteMs} UnattributedMs={UnattributedMs}",
                task.Definition.Value, task.Resource.Type, result, memorySize,
                totalElapsed.TotalMilliseconds, acquireElapsed.TotalMilliseconds,
                handlerElapsed.TotalMilliseconds, handlerApi.ApiElapsed.TotalMilliseconds,
                handlerApi.ApiCallCount, handlerNonApiElapsed.TotalMilliseconds,
                completeElapsed.TotalMilliseconds,
                unattributed.TotalMilliseconds);
        }
    }

    private static bool IsCompatible(LeasedCollectionTask task, CollectionDispatchCompatibilityKey expected)
    {
        if (!string.Equals(task.Resource.Provider, expected.Provider, StringComparison.Ordinal)
            || task.Lane != expected.Lane) return false;
        return expected.GroupKind switch
        {
            CollectionDispatchGroupKind.RaceDay => task.Resource.Type is CollectionResourceType.RaceCard or CollectionResourceType.RaceResult or CollectionResourceType.Race
                && task.EffectiveDate.HasValue
                && string.Equals(task.EffectiveDate.Value.ToString("yyyy-MM-dd"), expected.GroupKey,
                    StringComparison.Ordinal),
            CollectionDispatchGroupKind.WeekendSubjects => task.Resource.Type == CollectionResourceType.Horse
                && task.Attributes.TryGetValue("weekendPriorityUntil", out var weekend)
                && string.Equals(weekend, expected.GroupKey, StringComparison.Ordinal),
            _ => task.Definition == expected.Definition && task.EffectiveDate == expected.EffectiveDate,
        };
    }

    private async Task CompleteAsync(Guid taskId, string leaseToken, CollectionAttemptCompletion completion,
        CancellationToken cancellationToken)
    {
        using var completeResponse = await _client.PostAsJsonAsync(
            $"api/v2/internal/collection/tasks/{taskId}/attempts",
            new CompleteRequest(leaseToken, completion.Result, completion.ErrorCode, completion.ErrorMessage,
                completion.RequestedUrl?.ToString(), completion.FinalUrl?.ToString(), completion.HttpStatusCode,
                completion.PageIdentification, completion.RetryAt, completion.NextCollectionAt,
                completion.LocationOutcomes, completion.FailureImpact, completion.StageOutcomes,
                completion.RaceEvidence), cancellationToken)
            .ConfigureAwait(false);
        completeResponse.EnsureSuccessStatusCode();
    }

    private sealed record CompleteRequest(string LeaseToken, CollectionAttemptResult Result,
        string? ErrorCode, string? ErrorMessage, string? RequestedUrl, string? FinalUrl,
        int? HttpStatusCode, string? PageIdentification, DateTimeOffset? RetryAt,
        DateTimeOffset? NextCollectionAt, IReadOnlyList<ResourceLocationOutcome>? LocationOutcomes,
        CollectionFailureImpact FailureImpact, IReadOnlyList<CollectionStageOutcome>? StageOutcomes,
        RaceSchedulingEvidence? RaceEvidence);
}

public sealed class CollectionTaskActiveElsewhereException(Guid taskId)
    : Exception($"Collection task {taskId} is active in another worker.");

public sealed class CollectionDispatchCompatibilityException(Guid taskId)
    : Exception($"Collection task {taskId} did not match its dispatch envelope compatibility key.");
