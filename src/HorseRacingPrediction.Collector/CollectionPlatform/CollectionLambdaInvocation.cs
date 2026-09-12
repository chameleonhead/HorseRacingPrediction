using System.Text.Json;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Collector.CollectionPlatform;

public sealed record CollectionLambdaBatchItemFailure(string ItemIdentifier);

public sealed record CollectionLambdaBatchResponse(IReadOnlyList<CollectionLambdaBatchItemFailure> BatchItemFailures);

public static class CollectionLambdaInvocation
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
}
