using System.Net.Http.Json;
using System.Net;
using System.Reflection;
using System.Text.Json;

namespace HorseRacingPrediction.Collector.Scheduling;

public class HttpProcessingStateStoreProxy : DispatchProxy
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly AsyncLocal<TaskScope?> CurrentTask = new();
    private HttpClient _client = null!;

    public static IProcessingStateStore Create(HttpClient client)
    {
        var proxy = Create<IProcessingStateStore, HttpProcessingStateStoreProxy>();
        ((HttpProcessingStateStoreProxy)(object)proxy)._client = client;
        return proxy;
    }

    public static IDisposable BeginTaskScope(LeasedCollectionTask task)
    {
        if (CurrentTask.Value is not null)
            throw new InvalidOperationException("A collection task scope is already active.");
        CurrentTask.Value = new TaskScope(task);
        return new ScopeHandle();
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        args ??= [];

        var scope = CurrentTask.Value;
        if (scope is not null
            && targetMethod.Name == nameof(IProcessingStateStore.AcquireReadyJobsAsync)
            && string.Equals(args[0] as string, scope.Task.JobType, StringComparison.Ordinal))
        {
            if (scope.Consumed)
                return Task.FromResult<IReadOnlyList<AcquiredProcessingJob>>([]);
            scope.Consumed = true;
            return Task.FromResult<IReadOnlyList<AcquiredProcessingJob>>(
                [new AcquiredProcessingJob(scope.Task.DeduplicationKey, scope.Task.Payload)]);
        }

        if (scope is not null
            && targetMethod.Name == nameof(IProcessingStateStore.CompleteJobAsync)
            && MatchesScope(scope, args))
        {
            return InvokeScopedCompleteAsync(scope.Task, args.OfType<CancellationToken>().LastOrDefault());
        }

        if (scope is not null
            && targetMethod.Name == nameof(IProcessingStateStore.FailJobAsync)
            && MatchesScope(scope, args))
        {
            return InvokeScopedFailAsync(scope.Task, args[2] as string, args.OfType<CancellationToken>().LastOrDefault());
        }

        if (scope is not null
            && targetMethod.Name == nameof(IProcessingStateStore.RequeueJobAsync)
            && MatchesScope(scope, args))
        {
            var now = (DateTimeOffset)args[2]!;
            var error = args[3] as string;
            var availableAt = args.Length > 5 && args[5] is DateTimeOffset explicitAvailableAt ? explicitAvailableAt : now;
            return InvokeScopedRequeueAsync(scope.Task, availableAt, error, args.OfType<CancellationToken>().LastOrDefault());
        }

        var cancellationToken = args.OfType<CancellationToken>().LastOrDefault();
        var arguments = args
            .Where((_, index) => targetMethod.GetParameters()[index].ParameterType != typeof(CancellationToken))
            .Select(value => JsonSerializer.SerializeToElement(value, value?.GetType() ?? typeof(object), JsonOptions))
            .ToArray();
        var request = new ProcessingStateRpcRequest(targetMethod.Name, arguments);

        if (targetMethod.ReturnType == typeof(Task))
            return InvokeVoidAsync(targetMethod.Name, request, cancellationToken);

        var resultType = targetMethod.ReturnType.GetGenericArguments()[0];
        return typeof(HttpProcessingStateStoreProxy)
            .GetMethod(nameof(InvokeResultAsync), BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(resultType)
            .Invoke(this, [targetMethod.Name, request, cancellationToken]);
    }

    private static bool MatchesScope(TaskScope scope, object?[] args)
        => string.Equals(args[0] as string, scope.Task.JobType, StringComparison.Ordinal)
            && string.Equals(args[1] as string, scope.Task.DeduplicationKey, StringComparison.Ordinal);

    private Task<bool> InvokeScopedCompleteAsync(LeasedCollectionTask task, CancellationToken cancellationToken)
        => InvokeDirectAsync<bool>(
            nameof(IProcessingStateStore.CompleteCollectionTaskAsync),
            [task.JobType, task.DeduplicationKey, task.LeaseToken],
            cancellationToken);

    private async Task InvokeScopedFailAsync(LeasedCollectionTask task, string? error, CancellationToken cancellationToken)
    {
        var changed = await InvokeDirectAsync<bool>(
            nameof(IProcessingStateStore.FailCollectionTaskAsync),
            [task.JobType, task.DeduplicationKey, task.LeaseToken, error],
            cancellationToken).ConfigureAwait(false);
        if (!changed)
            throw new InvalidOperationException("The leased collection task could not be marked as failed.");
    }

    private Task<bool> InvokeScopedRequeueAsync(LeasedCollectionTask task, DateTimeOffset availableAt, string? error, CancellationToken cancellationToken)
        => InvokeDirectAsync<bool>(
            nameof(IProcessingStateStore.RequeueCollectionTaskAsync),
            [task.JobType, task.DeduplicationKey, task.LeaseToken, availableAt, error],
            cancellationToken);

    private Task<T> InvokeDirectAsync<T>(string method, object?[] arguments, CancellationToken cancellationToken)
    {
        var serialized = arguments
            .Select(value => JsonSerializer.SerializeToElement(value, value?.GetType() ?? typeof(object), JsonOptions))
            .ToArray();
        return InvokeResultAsync<T>(method, new ProcessingStateRpcRequest(method, serialized), cancellationToken);
    }

    private async Task InvokeVoidAsync(string method, ProcessingStateRpcRequest request, CancellationToken cancellationToken)
    {
        using var response = await _client.PostAsJsonAsync($"api/internal/collection/state/{method}", request, JsonOptions, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private async Task<T> InvokeResultAsync<T>(string method, ProcessingStateRpcRequest request, CancellationToken cancellationToken)
    {
        using var response = await _client.PostAsJsonAsync($"api/internal/collection/state/{method}", request, JsonOptions, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            return default!;

        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false))!;
    }

    private sealed class TaskScope(LeasedCollectionTask task)
    {
        public LeasedCollectionTask Task { get; } = task;
        public bool Consumed { get; set; }
    }

    private sealed class ScopeHandle : IDisposable
    {
        public void Dispose() => CurrentTask.Value = null;
    }
}
