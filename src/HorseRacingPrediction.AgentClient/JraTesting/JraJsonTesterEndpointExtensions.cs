namespace HorseRacingPrediction.AgentClient.JraTesting;

public static class JraJsonTesterEndpointExtensions
{
    public static IEndpointRouteBuilder MapJraJsonTesterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/tools/jra-tool",
            () => Results.Content(JraJsonTesterPageHtml.Content, "text/html; charset=utf-8"));

        endpoints.MapGet(
            "/tools/jra-json",
            () => Results.Redirect("/tools/jra-tool"));

        endpoints.MapGet(
            "/api/tools/jra-json",
            async (string url, bool includeSnapshot, JraJsonExtractionService service, CancellationToken cancellationToken) =>
            {
                try
                {
                    var result = await service.ExtractAsync(url, includeSnapshot, cancellationToken).ConfigureAwait(false);
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
                        title: "JRA URL の抽出に失敗しました。",
                        detail: ex.Message,
                        statusCode: StatusCodes.Status500InternalServerError);
                }
            });

        return endpoints;
    }
}
