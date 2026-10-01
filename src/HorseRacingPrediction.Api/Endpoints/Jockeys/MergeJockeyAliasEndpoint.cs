using EventFlow;
using HorseRacingPrediction.Application.Commands.Jockeys;
using HorseRacingPrediction.Domain.Jockeys;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Jockeys;
using HorseRacingPrediction.Contracts.Common;

namespace HorseRacingPrediction.Api.Endpoints.Jockeys;

internal static class MergeJockeyAliasEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/jockeys/{jockeyId}/aliases",
                    [SwaggerOperation(Summary = "Merge jockey alias", Description = "Adds or updates an alias for a jockey from an external data source")]
        async (string jockeyId, MergeJockeyAliasRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        if (request.Alias is null) return Results.BadRequest(new[] { "Alias payload is required." });
                        var alias = request.Alias;
                        var command = new MergeJockeyAliasCommand(
                            new JockeyId(jockeyId),
                            alias.AliasType,
                            alias.AliasValue,
                            alias.SourceName,
                            alias.IsPrimary);

                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        return result.IsSuccess
                            ? Results.Ok()
                            : Results.BadRequest(new[] { "Command execution failed." });
                    })
                    .WithName("MergeJockeyAlias")
                    .WithTags("Jockey API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
