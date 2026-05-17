namespace HorseRacingPrediction.AgentClient.Scheduling;

public static class AgentCollectionStatusEndpointExtensions
{
    public static IEndpointRouteBuilder MapAgentCollectionStatusEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/agent/race-collection-statuses",
            async (DateOnly from, DateOnly to, ProcessingStateStore stateStore, CancellationToken cancellationToken) =>
            {
                if (from > to)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["from"] = ["from は to 以下である必要があります。"]
                    });
                }

                var items = await stateStore.GetRaceDataCollectionStatusesAsync(from, to, cancellationToken).ConfigureAwait(false);
                return Results.Ok(items);
            });

        return endpoints;
    }
}