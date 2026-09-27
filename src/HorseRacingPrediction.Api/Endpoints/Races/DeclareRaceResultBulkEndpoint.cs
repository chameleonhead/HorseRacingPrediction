using EventFlow;
using EventFlow.EntityFramework;
using EventFlow.Queries;
using ApiContracts = HorseRacingPrediction.Contracts;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Infrastructure.Persistence;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Races;

using static HorseRacingPrediction.Api.Endpoints.Races.RaceResultBulkService;


internal static class DeclareRaceResultBulkEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/races/result-bulk",
                    [SwaggerOperation(Summary = "Declare race result in bulk", Description = "Creates/updates the race and declares result, entry results, weather, track condition and payouts in a single call")]
        async (ApiContracts.DeclareRaceResultBulkRequest request, ICommandBus commandBus, IQueryProcessor queryProcessor, IDbContextProvider<EventStoreDbContext> dbContextProvider, CollectionPlatformStore collectionStore, CancellationToken cancellationToken) =>
                    {
                        if (request is null) return Results.BadRequest(new[] { "Request is required." });
                        return await ApplyCollectedRaceResultBulkAsync(request, commandBus, queryProcessor,
                            dbContextProvider, collectionStore, cancellationToken).ConfigureAwait(false);

                    })
                    .WithName("DeclareRaceResultBulk")
                    .WithTags("Race API")
                    .Produces<ApiContracts.DeclareRaceResultBulkResponse>(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
