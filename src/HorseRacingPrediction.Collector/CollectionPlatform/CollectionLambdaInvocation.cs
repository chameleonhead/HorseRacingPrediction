using System.Text.Json;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Collector.CollectionPlatform;

public sealed record CollectionLambdaBatchItemFailure(string ItemIdentifier);

public sealed record CollectionLambdaBatchResponse(IReadOnlyList<CollectionLambdaBatchItemFailure> BatchItemFailures);

public static class CollectionLambdaInvocation
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<CollectionLambdaBatchResponse> ExecuteWakeAsync(string eventJson,
        CollectionPlatformWorkerClient worker, Func<bool>? canStartTask = null,
        CancellationToken cancellationToken = default,
        Func<CollectionDispatchEnvelope, Func<CancellationToken, Task>, CancellationToken, Task>? executeGroup = null,
        string? lambdaRequestId = null)
    {
        using var document = JsonDocument.Parse(eventJson);
        if ((!document.RootElement.TryGetProperty("Records", out var records)
             && !document.RootElement.TryGetProperty("records", out records))
            || records.ValueKind != JsonValueKind.Array)
            throw new JsonException("The Lambda event did not contain an SQS Records array.");
        var failures = new List<CollectionLambdaBatchItemFailure>();
        foreach (var record in records.EnumerateArray())
        {
            if (!record.TryGetProperty("messageId", out var messageIdElement)
                || messageIdElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(messageIdElement.GetString()))
                throw new JsonException("An SQS record did not contain a messageId.");
            var messageId = messageIdElement.GetString()!;
            if (!TryReadWake(record, out var wake))
            {
                // Legacy task envelopes are deliberately acknowledged during cutover. Their current
                // Ready outbox rows are re-opened by schema migration 14 and receive a fresh wake.
                if (!TryReadEnvelope(record, out _)) failures.Add(new(messageId));
                continue;
            }
            if (cancellationToken.IsCancellationRequested || canStartTask?.Invoke() == false) continue;

            CollectionExecutionAcquireResult acquired;
            try
            {
                acquired = await worker.AcquireNextAsync(wake!, messageId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // A wake is a hint, not the durable work item. The DB scanner emits another after
                // the short reservation expires, so transport failures must not hold SQS visibility.
                continue;
            }
            if (acquired.Status == CollectionExecutionAcquireStatus.NoWork || acquired.Envelope is null
                || acquired.ExecutionBatchId is null || string.IsNullOrWhiteSpace(acquired.LeaseToken)) continue;

            try
            {
                await worker.StartExecutionAsync(acquired.ExecutionBatchId.Value, acquired.LeaseToken,
                    lambdaRequestId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // The start response can be ambiguous. Do not run Playwright and do not complete the
                // batch; the short StartPending lease is the fencing/recovery mechanism.
                continue;
            }

            try
            {
                async Task ProcessAsync(CancellationToken token)
                {
                    for (var index = 0; index < acquired.Envelope.Tasks.Count; index++)
                    {
                        if (token.IsCancellationRequested || canStartTask?.Invoke() == false) break;
                        var task = acquired.Envelope.Tasks[index];
                        using var correlation = CollectionAttemptCorrelationScope.Push(new(
                            acquired.ExecutionBatchId.Value, acquired.Envelope.EnvelopeId, messageId,
                            lambdaRequestId, index + 1, acquired.Envelope.Tasks.Count));
                        try
                        {
                            await worker.ExecuteAsync(new(task.TaskId, task.DispatchGeneration), token).ConfigureAwait(false);
                        }
                        catch (CollectionTaskActiveElsewhereException)
                        {
                            // Another worker owns this task lease. Keep the execution batch alive so
                            // later task references can be processed, while asking SQS to redeliver the
                            // wake for the unresolved task after the competing lease is released.
                            if (failures.All(x => x.ItemIdentifier != messageId))
                                failures.Add(new(messageId));
                        }
                    }
                }
                if (executeGroup is null) await ProcessAsync(cancellationToken).ConfigureAwait(false);
                else await executeGroup(acquired.Envelope, ProcessAsync, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                using var finalize = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await worker.CompleteExecutionAsync(acquired.ExecutionBatchId.Value, acquired.LeaseToken,
                    finalize.Token).ConfigureAwait(false);
            }
        }
        return new(failures);
    }

    public static async Task<CollectionLambdaBatchResponse> ExecuteAsync(string eventJson,
        Func<CollectionTaskNotification, CancellationToken, Task> executeTask,
        Func<bool>? canStartTask = null, CancellationToken cancellationToken = default,
        Func<CollectionDispatchEnvelope, Func<CancellationToken, Task>, CancellationToken, Task>? executeGroup = null,
        string? lambdaRequestId = null)
    {
        using var document = JsonDocument.Parse(eventJson);
        if ((!document.RootElement.TryGetProperty("Records", out var records)
             && !document.RootElement.TryGetProperty("records", out records))
            || records.ValueKind != JsonValueKind.Array)
            throw new JsonException("The Lambda event did not contain an SQS Records array.");

        var failures = new List<CollectionLambdaBatchItemFailure>();
        foreach (var record in records.EnumerateArray())
        {
            if (!record.TryGetProperty("messageId", out var messageIdElement)
                || messageIdElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(messageIdElement.GetString()))
                throw new JsonException("An SQS record did not contain a messageId.");
            var messageId = messageIdElement.GetString()!;
            var executionBatchId = Guid.NewGuid();

            if (cancellationToken.IsCancellationRequested || canStartTask?.Invoke() == false
                || !TryReadEnvelope(record, out var envelope))
            {
                failures.Add(new(messageId));
                continue;
            }

            try
            {
                if (executeGroup is null)
                    await ProcessEnvelopeAsync(cancellationToken).ConfigureAwait(false);
                else
                    await executeGroup(envelope!, ProcessEnvelopeAsync, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                AddFailure();
            }

            async Task ProcessEnvelopeAsync(CancellationToken token)
            {
                for (var index = 0; index < envelope!.Tasks.Count; index++)
                {
                    var task = envelope.Tasks[index];
                    if (token.IsCancellationRequested || canStartTask?.Invoke() == false)
                    {
                        AddFailure();
                        break;
                    }

                    try
                    {
                        using var correlation = CollectionAttemptCorrelationScope.Push(new(executionBatchId,
                            envelope.EnvelopeId, messageId, lambdaRequestId, index + 1, envelope.Tasks.Count));
                        await executeTask(new(task.TaskId, task.DispatchGeneration), token)
                            .ConfigureAwait(false);
                    }
                    catch (CollectionTaskActiveElsewhereException)
                    {
                        AddFailure();
                    }
                    catch (Exception)
                    {
                        AddFailure();
                        break;
                    }
                }
            }

            void AddFailure()
            {
                if (failures.All(x => x.ItemIdentifier != messageId)) failures.Add(new(messageId));
            }
        }

        return new(failures);
    }

    public static string SerializeResponse(CollectionLambdaBatchResponse response)
        => JsonSerializer.Serialize(response, JsonOptions);

    private static bool TryReadEnvelope(JsonElement record, out CollectionDispatchEnvelope? envelope)
    {
        envelope = null;
        try
        {
            if (!record.TryGetProperty("body", out var bodyElement) || bodyElement.ValueKind != JsonValueKind.String)
                return false;
            envelope = JsonSerializer.Deserialize<CollectionDispatchEnvelope>(bodyElement.GetString()!, JsonOptions);
            return envelope?.IsSupported() == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadWake(JsonElement record, out CollectionWakeSignal? wake)
    {
        wake = null;
        try
        {
            if (!record.TryGetProperty("body", out var bodyElement) || bodyElement.ValueKind != JsonValueKind.String)
                return false;
            wake = JsonSerializer.Deserialize<CollectionWakeSignal>(bodyElement.GetString()!, JsonOptions);
            return wake is { ContractVersion: 1 } && wake.WakeId != Guid.Empty
                && wake.DispatchEnvelopeId != Guid.Empty && !string.IsNullOrWhiteSpace(wake.ReservationToken);
        }
        catch (JsonException) { return false; }
    }
}
