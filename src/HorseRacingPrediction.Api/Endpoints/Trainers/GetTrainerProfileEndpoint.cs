using EventFlow.Queries;
using HorseRacingPrediction.Application.Queries.ReadModels;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Trainers;

namespace HorseRacingPrediction.Api.Endpoints.Trainers;

internal static class GetTrainerProfileEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/trainers/{trainerId}",
                    [SwaggerOperation(Summary = "Get trainer profile", Description = "Returns trainer profile read model")]
        async ([AsParameters] GetTrainerProfileRequest request, IQueryProcessor queryProcessor, CancellationToken cancellationToken) =>
                    {
                        var trainerId = request.TrainerId;
                        var query = new ReadModelByIdQuery<TrainerReadModel>(trainerId);
                        var readModel = await queryProcessor.ProcessAsync(query, cancellationToken).ConfigureAwait(false);

                        if (readModel is null || string.IsNullOrEmpty(readModel.TrainerId))
                            return Results.NotFound();

                        var response = new TrainerDto
                        {
                            TrainerId = readModel.TrainerId,
                            DisplayName = readModel.DisplayName,
                            NormalizedName = readModel.NormalizedName,
                            AffiliationCode = readModel.AffiliationCode,
                            Aliases = readModel.Aliases
                                .Select(a => new AliasDto(a.AliasType, a.AliasValue, a.SourceName, a.IsPrimary))
                                .ToList()
                        };

                        return Results.Ok(new GetTrainerProfileResponse(response));
                    })
                    .WithName("GetTrainerProfile")
                    .WithTags("Trainer API")
                    .Produces<GetTrainerProfileResponse>(StatusCodes.Status200OK)
                    .Produces(StatusCodes.Status404NotFound);
    }
}
