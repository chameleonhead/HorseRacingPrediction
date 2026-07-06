namespace HorseRacingPrediction.Collector.JraTesting;

public static class JraJsonTesterEndpointExtensions
{
    public static IEndpointRouteBuilder MapJraJsonTesterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/tools/jra-json",
            async (string url, bool includeSnapshot, bool? headless, JraJsonExtractionService service, CancellationToken cancellationToken) =>
            {
                try
                {
                    var result = await service
                        .ExtractAsync(url, includeSnapshot, headless: headless ?? true, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    return Results.Ok(result);
                }
                catch (ArgumentException ex)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["url"] = new[] { ex.Message }
                    });
                }
                catch (Exception ex)
                {
                    return Results.Problem(
                        title: "URL の抽出に失敗しました。",
                        detail: ex.Message,
                        statusCode: StatusCodes.Status500InternalServerError);
                }
            });

        return endpoints;
    }
}
