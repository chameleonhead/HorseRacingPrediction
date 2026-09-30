using EventFlow;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Domain.Horses;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Horses;

namespace HorseRacingPrediction.Api.Endpoints.Horses;

internal static class UpdateHorseProfileEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/horses/{horseId}",
                    [SwaggerOperation(Summary = "Update horse profile", Description = "Updates profile information of an existing horse")]
        async (string horseId, UpdateHorseProfileRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        var command = new UpdateHorseProfileCommand(
                            new HorseId(horseId),
                            request.RegisteredName,
                            request.NormalizedName,
                            request.SexCode,
                            request.BirthDate,
                            request.OwnerName, request.BreederName, request.SireName, request.DamName,
                            request.DamsireName, request.CoatColor);

                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        return result.IsSuccess
                            ? Results.Ok()
                            : Results.BadRequest(new[] { "Command execution failed." });
                    })
                    .WithName("UpdateHorseProfile")
                    .WithTags("Horse API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
