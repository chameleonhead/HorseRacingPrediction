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

    // JSON API は例外なくすべて /api 配下に置く規約とし（EndpointExtensions.cs）、
    // 管理UIは /admin プレフィックスを使わずルート直下（/races, /horses など）で提供する。
    // 両者のパスが重ならないため、UI側のルートだけを明示的に認証免除すればよい。
    private static readonly string[] AdminUiRootSegments =
    {
        "races", "horses", "jockeys", "trainers", "predictions",
    };

    private static bool IsAnonymousPath(PathString path)
    {
        if (path.Equals("/health", StringComparison.OrdinalIgnoreCase)
            || path.Equals(PathString.Empty)
            || path.Equals("/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/login", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/logout", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/app.css", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/_blazor", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/_framework", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var rootSegment in AdminUiRootSegments)
        {
            if (path.StartsWithSegments("/" + rootSegment, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
