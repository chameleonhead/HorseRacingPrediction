using System.Text.Json;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Collection;

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
            if (cancellationToken.IsCancellationRequested || canStartTask?.Invoke() == false)
            {
                failures.Add(new(messageId));
                continue;
            }

            CollectionExecutionAcquireResult acquired;
            try
            {
                acquired = await worker.AcquireNextAsync(wake!, messageId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                // Acquire may have committed before the response was lost. Retrying this wake is safe:
                // the API's lease fence recognizes a duplicate acquire, and SQS preserves the item.
                failures.Add(new(messageId));
                continue;
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
            {
                failures.Add(new(messageId));
                continue;
            }
            if (acquired.Status == CollectionExecutionAcquireStatus.NoWork) continue;
            if (acquired.Status != CollectionExecutionAcquireStatus.Acquired || acquired.Envelope is null
                || acquired.ExecutionBatchId is null || string.IsNullOrWhiteSpace(acquired.LeaseToken))
            {
                failures.Add(new(messageId));
                continue;
            }
            var envelope = acquired.Envelope;
            var executionBatchId = acquired.ExecutionBatchId.Value;
            var leaseToken = acquired.LeaseToken;

            try
            {
                await worker.StartExecutionAsync(executionBatchId, leaseToken,
                    lambdaRequestId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                // The start response can be ambiguous. Do not run Playwright and do not complete the
                // batch; the short StartPending lease is the fencing/recovery mechanism.
                failures.Add(new(messageId));
                continue;
            }

            try
            {
                async Task ProcessAsync(CancellationToken token)
                {
                    for (var index = 0; index < envelope.Tasks.Count; index++)
                    {
                        if (token.IsCancellationRequested || canStartTask?.Invoke() == false) break;
                        var task = envelope.Tasks[index];
                        using var correlation = CollectionAttemptCorrelationScope.Push(new(
                            executionBatchId, envelope.EnvelopeId, messageId,
                            lambdaRequestId, index + 1, envelope.Tasks.Count));
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
                else await executeGroup(envelope, ProcessAsync, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                using var finalize = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await worker.CompleteExecutionAsync(executionBatchId, leaseToken,
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
