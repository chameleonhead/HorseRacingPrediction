using EventFlow.Queries;
using ApiContracts = HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Api.Security;
using AppReadModels = HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Infrastructure.Persistence;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Races;

using static HorseRacingPrediction.Api.Endpoints.Races.RaceEndpointMappings;


internal static class GetRacePredictionContextEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/races/{raceId}/context",
                    [SwaggerOperation(Summary = "Get race prediction context", Description = "Returns prediction context read model including entries, weather and track conditions")]
        async (string raceId, IQueryProcessor queryProcessor, RaceWriteCoordinator coordinator, CancellationToken cancellationToken) =>
                    {
                        var barrier = await coordinator.ReadBarrierAsync(raceId, cancellationToken);
                        if (barrier is { Verified: false }) return Results.Conflict(new { code = "RaceRepairPending" });
                        var query = new ReadModelByIdQuery<AppReadModels.RacePredictionContextReadModel>(raceId);
                        var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                        if (readModel is null || string.IsNullOrEmpty(readModel.RaceId))
                            return Results.NotFound();

                        var result = ToAgentRacePredictionContext(readModel);
                        result.EntryAssignmentFingerprint = await coordinator.AssignmentFingerprintAsync(raceId, cancellationToken);
                        return Results.Ok(result);
                    })
                    .AddEndpointFilter<RacePredictionReadEndpointFilter>()
                    .WithName("GetRacePredictionContext")
                    .WithTags("Race API")
                    .Produces<HorseRacingPrediction.Contracts.Races.RacePredictionContextDto>(StatusCodes.Status200OK)
                    .Produces(StatusCodes.Status404NotFound);
    }
}
