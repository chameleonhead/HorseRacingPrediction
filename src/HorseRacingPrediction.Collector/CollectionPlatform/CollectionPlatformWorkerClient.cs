using System.Net.Http.Json;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

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
            completion = await handlers.Resolve(task.Definition, task.Resource.Type)
                .CollectAsync(task, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            completion = new(CollectionAttemptResult.PermanentFailure, ex.GetType().Name, ex.Message);
        }

        using var completeResponse = await client.PostAsJsonAsync(
            $"api/internal/collection/tasks/{notification.TaskId}/complete",
            new CompleteRequest(task.LeaseToken, completion.Result, completion.ErrorCode, completion.ErrorMessage,
                completion.RequestedUrl?.ToString(), completion.FinalUrl?.ToString(), completion.HttpStatusCode,
                completion.PageIdentification, completion.RetryAt, completion.NextCollectionAt), cancellationToken)
            .ConfigureAwait(false);
        completeResponse.EnsureSuccessStatusCode();
    }

    private sealed record CompleteRequest(string LeaseToken, CollectionAttemptResult Result,
        string? ErrorCode, string? ErrorMessage, string? RequestedUrl, string? FinalUrl,
        int? HttpStatusCode, string? PageIdentification, DateTimeOffset? RetryAt,
        DateTimeOffset? NextCollectionAt);
}
