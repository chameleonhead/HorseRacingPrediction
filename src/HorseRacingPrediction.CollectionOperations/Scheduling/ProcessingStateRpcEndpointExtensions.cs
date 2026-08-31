using System.Reflection;
using System.Text.Json;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record ProcessingStateRpcRequest(string Method, JsonElement[] Arguments);

public static class ProcessingStateRpcEndpointExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapProcessingStateRpcEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/internal/collection/state/{method}", InvokeAsync)
            .ExcludeFromDescription();
        return app;
    }

    private static async Task<IResult> InvokeAsync(
        string method,
        ProcessingStateRpcRequest request,
        ProcessingStateStore store,
        HttpContext context)
    {
        if (!string.Equals(method, request.Method, StringComparison.Ordinal))
            return Results.BadRequest("RPC method mismatch.");

        var target = typeof(IProcessingStateStore).GetMethod(method);
        if (target is null)
            return Results.NotFound();

        var parameters = target.GetParameters();
        var serializedIndex = 0;
        var arguments = new object?[parameters.Length];
        for (var index = 0; index < parameters.Length; index++)
        {
            if (parameters[index].ParameterType == typeof(CancellationToken))
            {
                arguments[index] = context.RequestAborted;
                continue;
            }

            if (serializedIndex >= request.Arguments.Length)
                return Results.BadRequest($"Missing argument '{parameters[index].Name}'.");

            arguments[index] = request.Arguments[serializedIndex++].Deserialize(parameters[index].ParameterType, JsonOptions);
        }

        if (serializedIndex != request.Arguments.Length)
            return Results.BadRequest("Too many RPC arguments.");

        try
        {
            var task = (Task?)target.Invoke(store, arguments)
                ?? throw new InvalidOperationException("State store method did not return a Task.");
            await task.ConfigureAwait(false);

            if (!target.ReturnType.IsGenericType)
                return Results.NoContent();

            var result = task.GetType().GetProperty("Result")?.GetValue(task);
            return Results.Json(result, JsonOptions);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }
}
