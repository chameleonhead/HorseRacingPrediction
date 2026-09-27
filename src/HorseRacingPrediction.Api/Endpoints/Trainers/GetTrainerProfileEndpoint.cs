using EventFlow.Queries;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Application.Queries.ReadModels;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Trainers;

internal static class GetTrainerProfileEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/trainers/{trainerId}",
                    [SwaggerOperation(Summary = "Get trainer profile", Description = "Returns trainer profile read model")]
        async (string trainerId, IQueryProcessor queryProcessor, CancellationToken cancellationToken) =>
                    {
                        var query = new ReadModelByIdQuery<TrainerReadModel>(trainerId);
                        var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                        if (readModel is null || string.IsNullOrEmpty(readModel.TrainerId))
                            return Results.NotFound();

                        var response = new TrainerProfileResponse(
                            readModel.TrainerId,
                            readModel.DisplayName,
                            readModel.NormalizedName,
                            readModel.AffiliationCode,
                            readModel.Aliases
                                .Select(a => new AliasResponse(a.AliasType, a.AliasValue, a.SourceName, a.IsPrimary))
                                .ToList());

                        return Results.Ok(response);
                    })
                    .WithName("GetTrainerProfile")
                    .WithTags("Trainer API")
                    .Produces<TrainerProfileResponse>(StatusCodes.Status200OK)
                    .Produces(StatusCodes.Status404NotFound);
    }
}
