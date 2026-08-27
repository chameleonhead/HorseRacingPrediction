using HorseRacingPrediction.Collector.Scheduling;

namespace HorseRacingPrediction.Collector.Scheduling;

public static class AgentAcquisitionStatusEndpointExtensions
{
    public static IEndpointRouteBuilder MapAgentAcquisitionStatusEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/collection/acquisitions",
            async (
                DateOnly from,
                DateOnly to,
                AgentAcquisitionSubjectType? subjectType,
                RaceDataCollectionState? status,
                IProcessingStateStore stateStore,
                CancellationToken cancellationToken) =>
            {
                if (from > to)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["from"] = ["from は to 以下である必要があります。"]
                    });
                }

                var items = await stateStore
                    .GetAgentAcquisitionStatusesAsync(from, to, subjectType, status, cancellationToken)
                    .ConfigureAwait(false);
                return Results.Ok(items);
            })
            .WithName("GetCollectionAcquisitions")
            .WithTags("Collection Tasks API")
            .WithSummary("Get collection acquisition statuses");

        return endpoints;
    }
}
