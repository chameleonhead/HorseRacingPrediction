using EventFlow;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Domain.Horses;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Horses;

namespace HorseRacingPrediction.Api.Endpoints.Horses;

internal static class CorrectHorseDataEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/horses/{horseId}",
                    [SwaggerOperation(Summary = "Correct horse data", Description = "Corrects horse master data with an optional audit reason")]
        async (string horseId, CorrectHorseDataRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        if (request.Horse is null) return Results.BadRequest(new[] { "Horse payload is required." });
                        var correction = request.Horse;
                        var command = new CorrectHorseDataCommand(
                            new HorseId(horseId),
                            correction.RegisteredName,
                            correction.NormalizedName,
                            correction.SexCode,
                            correction.BirthDate,
                            correction.Reason);

                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        return result.IsSuccess
                            ? Results.Ok()
                            : Results.BadRequest(new[] { "Command execution failed." });
                    })
                    .WithName("CorrectHorseData")
                    .WithTags("Horse API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
