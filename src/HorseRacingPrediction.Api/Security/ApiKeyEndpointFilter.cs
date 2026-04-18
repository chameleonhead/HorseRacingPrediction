using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Security;

public sealed class ApiKeyEndpointFilter : IEndpointFilter
{
    private readonly ApiKeyOptions _options;

    public ApiKeyEndpointFilter(IOptions<ApiKeyOptions> options)
    {
        _options = options.Value;
    }

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (string.IsNullOrWhiteSpace(_options.Key))
        {
            return ValueTask.FromResult<object?>(Results.Problem(
                detail: "API key is not configured. Set ApiKey:Key or HORSE_RACING_API_KEY.",
                statusCode: StatusCodes.Status500InternalServerError));
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(_options.HeaderName, out var provided))
        {
            return ValueTask.FromResult<object?>(Results.Unauthorized());
        }

        if (!string.Equals(provided.ToString(), _options.Key, StringComparison.Ordinal))
        {
            return ValueTask.FromResult<object?>(Results.Unauthorized());
        }

        return next(context);
    }
}
