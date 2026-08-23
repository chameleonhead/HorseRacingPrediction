using System.Net.Http.Json;
using System.Net;
using System.Reflection;
using System.Text.Json;

namespace HorseRacingPrediction.Collector.Scheduling;

public class HttpProcessingStateStoreProxy : DispatchProxy
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private HttpClient _client = null!;

    public static IProcessingStateStore Create(HttpClient client)
    {
        var proxy = Create<IProcessingStateStore, HttpProcessingStateStoreProxy>();
        ((HttpProcessingStateStoreProxy)(object)proxy)._client = client;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        args ??= [];

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
}
