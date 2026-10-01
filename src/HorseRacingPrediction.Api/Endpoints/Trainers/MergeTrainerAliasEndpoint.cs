using EventFlow;
using HorseRacingPrediction.Application.Commands.Trainers;
using HorseRacingPrediction.Domain.Trainers;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Trainers;
using HorseRacingPrediction.Contracts.Common;

namespace HorseRacingPrediction.Api.Endpoints.Trainers;

internal static class MergeTrainerAliasEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/trainers/{trainerId}/aliases",
                    [SwaggerOperation(Summary = "Merge trainer alias", Description = "Adds or updates an alias for a trainer from an external data source")]
        async (string trainerId, MergeTrainerAliasRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        if (request.Alias is null) return Results.BadRequest(new[] { "Alias payload is required." });
                        var alias = request.Alias;
                        var command = new MergeTrainerAliasCommand(
                            new TrainerId(trainerId),
                            alias.AliasType,
                            alias.AliasValue,
                            alias.SourceName,
                            alias.IsPrimary);

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
