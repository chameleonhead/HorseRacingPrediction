namespace HorseRacingPrediction.Api.Endpoints.Health;


internal static class GetHealthEndpoint
{
    internal static void Map(WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { Status = "ok" }))
                    .WithName("Health")
                    .WithTags("Health")
                    .WithSummary("Health check");
    }
}
