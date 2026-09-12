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
            new { notification.DispatchGeneration, LeaseSeconds = 900 }, cancellationToken).ConfigureAwait(false);
        if (acquireResponse.StatusCode == System.Net.HttpStatusCode.Conflict) return;
        acquireResponse.EnsureSuccessStatusCode();
        var task = await acquireResponse.Content.ReadFromJsonAsync<LeasedCollectionTask>(cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Collection task lease response was empty.");

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
