using EventFlow.EntityFramework;
using EventFlow.Queries;
using ApiContracts = HorseRacingPrediction.Contracts;
using AppReadModels = HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Horses;

using static HorseRacingPrediction.Api.Endpoints.Horses.HorseEndpointMappings;


internal static class GetHorseProfileEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/horses/{horseId}",
                    [SwaggerOperation(Summary = "Get horse profile", Description = "Returns horse profile read model")]
        async (string horseId, IQueryProcessor queryProcessor,
                        IDbContextProvider<EventStoreDbContext> dbContextProvider, CancellationToken cancellationToken) =>
                    {
                        using (var dbContext = dbContextProvider.CreateContext())
                        {
                            var redirect = await dbContext.HorseIdentityRepairRedirects.AsNoTracking()
                                .SingleOrDefaultAsync(x => x.SourceHorseId == horseId, cancellationToken).ConfigureAwait(false);
                            if (redirect is not null) horseId = redirect.TargetHorseId;
                        }
                        var query = new ReadModelByIdQuery<AppReadModels.HorseReadModel>(horseId);
                        var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                        if (readModel is null || string.IsNullOrEmpty(readModel.HorseId))
                            return Results.NotFound();

                        return Results.Ok(ToAgentHorse(readModel));
                    })
                    .WithName("GetHorseProfile")
                    .WithTags("Horse API")
                    .Produces<ApiContracts.HorseReadDto>(StatusCodes.Status200OK)
                    .Produces(StatusCodes.Status404NotFound);
    }
}
