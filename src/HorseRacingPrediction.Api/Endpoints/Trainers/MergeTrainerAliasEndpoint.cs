using EventFlow;
using HorseRacingPrediction.Application.Commands.Trainers;
using HorseRacingPrediction.Domain.Trainers;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Common;

namespace HorseRacingPrediction.Api.Endpoints.Trainers;

internal static class MergeTrainerAliasEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/trainers/{trainerId}/aliases",
                    [SwaggerOperation(Summary = "Merge trainer alias", Description = "Adds or updates an alias for a trainer from an external data source")]
        async (string trainerId, MergeAliasRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        var command = new MergeTrainerAliasCommand(
                            new TrainerId(trainerId),
                            request.AliasType,
                            request.AliasValue,
                            request.SourceName,
                            request.IsPrimary);

                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        return result.IsSuccess
                            ? Results.Ok()
                            : Results.BadRequest(new[] { "Command execution failed." });
                    })
                    .WithName("MergeTrainerAlias")
                    .WithTags("Trainer API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
