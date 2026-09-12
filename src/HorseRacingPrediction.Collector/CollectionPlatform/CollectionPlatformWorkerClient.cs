using System.Net.Http.Json;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.Http;

namespace HorseRacingPrediction.Collector.CollectionPlatform;

public sealed class CollectionPlatformWorkerClient(HttpClient client,
    CollectionDefinitionHandlerRegistry handlers)
{
    public async Task ExecuteAsync(CollectionTaskNotification notification, CancellationToken cancellationToken)
    {
        using var acquireResponse = await client.PostAsJsonAsync(
            $"api/internal/collection/tasks/{notification.TaskId}/acquire",
            new { notification.DispatchGeneration, LeaseSeconds = 900,
                Correlation = CollectionAttemptCorrelationScope.Current }, cancellationToken).ConfigureAwait(false);
        acquireResponse.EnsureSuccessStatusCode();
        var acquire = await acquireResponse.Content.ReadFromJsonAsync<CollectionTaskAcquireResult>(cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Collection task acquire response was empty.");
        if (acquire.Status is CollectionTaskAcquireStatus.AlreadyTerminal
            or CollectionTaskAcquireStatus.SupersededGeneration) return;
        if (acquire.Status == CollectionTaskAcquireStatus.ActiveElsewhere)
            throw new CollectionTaskActiveElsewhereException(notification.TaskId);
        var task = acquire.Task ?? throw new InvalidOperationException("Acquired task lease was empty.");

        var expected = JraSessionExecutionScope.CurrentCompatibilityKey;
        if (expected is not null && !IsCompatible(task, expected))
            throw new CollectionDispatchCompatibilityException(notification.TaskId);

        CollectionAttemptCompletion completion;
        try
        {
            using var leaseScope = CollectionWorkerLeaseContext.Push(task.TaskId, task.LeaseToken);
            completion = await handlers.Resolve(task.Definition, task.Resource.Type)
                .CollectAsync(task, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            using var report = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await CompleteAsync(notification.TaskId, task.LeaseToken,
                new(CollectionAttemptResult.TransientFailure, "CollectorTimeout",
                    "Collector execution was cancelled or reached its deadline.",
                    RetryAt: DateTimeOffset.UtcNow.AddMinutes(1)), report.Token).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            completion = CollectionAttemptFailureClassifier.FromException(ex);
        }

        await CompleteAsync(notification.TaskId, task.LeaseToken, completion, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsCompatible(LeasedCollectionTask task, CollectionDispatchCompatibilityKey expected)
        => string.Equals(task.Resource.Provider, expected.Provider, StringComparison.Ordinal)
           && task.Definition == expected.Definition
           && task.EffectiveDate == expected.EffectiveDate
           && task.Lane == expected.Lane;

    private async Task CompleteAsync(Guid taskId, string leaseToken, CollectionAttemptCompletion completion,
        CancellationToken cancellationToken)
    {
        using var completeResponse = await client.PostAsJsonAsync(
            $"api/internal/collection/tasks/{taskId}/complete",
            new CompleteRequest(leaseToken, completion.Result, completion.ErrorCode, completion.ErrorMessage,
                completion.RequestedUrl?.ToString(), completion.FinalUrl?.ToString(), completion.HttpStatusCode,
                completion.PageIdentification, completion.RetryAt, completion.NextCollectionAt,
                completion.LocationOutcomes), cancellationToken)
            .ConfigureAwait(false);
        completeResponse.EnsureSuccessStatusCode();
    }

    private sealed record CompleteRequest(string LeaseToken, CollectionAttemptResult Result,
        string? ErrorCode, string? ErrorMessage, string? RequestedUrl, string? FinalUrl,
        int? HttpStatusCode, string? PageIdentification, DateTimeOffset? RetryAt,
        DateTimeOffset? NextCollectionAt, IReadOnlyList<ResourceLocationOutcome>? LocationOutcomes);
}

public sealed class CollectionTaskActiveElsewhereException(Guid taskId)
    : Exception($"Collection task {taskId} is active in another worker.");

public sealed class CollectionDispatchCompatibilityException(Guid taskId)
    : Exception($"Collection task {taskId} did not match its dispatch envelope compatibility key.");
