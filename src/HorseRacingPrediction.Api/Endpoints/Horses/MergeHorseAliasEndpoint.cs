using EventFlow;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Domain.Horses;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Common;

namespace HorseRacingPrediction.Api.Endpoints.Horses;

internal static class MergeHorseAliasEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/horses/{horseId}/aliases",
                    [SwaggerOperation(Summary = "Merge horse alias", Description = "Adds or updates an alias for a horse from an external data source")]
        async (string horseId, MergeAliasRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        var command = new MergeHorseAliasCommand(
                            new HorseId(horseId),
                            request.AliasType,
                            request.AliasValue,
                            request.SourceName,
                            request.IsPrimary);

                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        return result.IsSuccess
                            ? Results.Ok()
                            : Results.BadRequest(new[] { "Command execution failed." });
                    })
                    .WithName("MergeHorseAlias")
                    .WithTags("Horse API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
