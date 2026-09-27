using EventFlow;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Domain.Horses;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Horses;

internal static class CorrectHorseDataEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/horses/{horseId}",
                    [SwaggerOperation(Summary = "Correct horse data", Description = "Corrects horse master data with an optional audit reason")]
        async (string horseId, CorrectHorseDataRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        var command = new CorrectHorseDataCommand(
                            new HorseId(horseId),
                            request.RegisteredName,
                            request.NormalizedName,
                            request.SexCode,
                            request.BirthDate,
                            request.Reason);

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
