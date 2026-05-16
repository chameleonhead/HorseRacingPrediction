using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Security;

public static class ApiKeyApplicationBuilderExtensions
{
    public static IApplicationBuilder UseApiKeyProtection(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (IsAnonymousPath(context.Request.Path))
            {
                await next().ConfigureAwait(false);
                return;
            }

            var options = context.RequestServices.GetRequiredService<IOptions<ApiKeyOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.Key))
            {
                await Results.Problem(
                        detail: "API key is not configured. Set ApiKey:Key or HORSE_RACING_API_KEY.",
                        statusCode: StatusCodes.Status500InternalServerError)
                    .ExecuteAsync(context)
                    .ConfigureAwait(false);
                return;
            }

            if (!context.Request.Headers.TryGetValue(options.HeaderName, out var provided) ||
                !string.Equals(provided.ToString(), options.Key, StringComparison.Ordinal))
            {
                await Results.Unauthorized().ExecuteAsync(context).ConfigureAwait(false);
                return;
            }

            await next().ConfigureAwait(false);
        });
    }

    private static bool IsAnonymousPath(PathString path)
    {
        return path.Equals("/health", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase);
    }
}