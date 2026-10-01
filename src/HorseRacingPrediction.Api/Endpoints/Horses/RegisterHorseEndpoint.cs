using EventFlow;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Domain.Horses;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Horses;

namespace HorseRacingPrediction.Api.Endpoints.Horses;

internal static class RegisterHorseEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/horses",
                    [SwaggerOperation(Summary = "Register horse", Description = "Registers a new horse")]
        async (RegisterHorseRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        if (request.Horse is null) return Results.BadRequest(new[] { "Horse payload is required." });
                        var horse = request.Horse;
                        try
                        {
                            var horseId = string.IsNullOrWhiteSpace(horse.HorseId) ? HorseId.New : new HorseId(horse.HorseId);
                            var command = new RegisterHorseCommand(
                                horseId,
                                horse.RegisteredName,
                                horse.NormalizedName,
                                horse.SexCode,
                                horse.BirthDate,
                                horse.OwnerName, horse.BreederName, horse.SireName, horse.DamName,
                                horse.DamsireName, horse.CoatColor);

                            var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                            return result.IsSuccess
                                ? Results.Created($"/api/horses/{horseId.Value}", new RegisterHorseResponse(horseId.Value))
                                : Results.BadRequest(new[] { "Command execution failed." });
                        }
                        catch (InvalidOperationException ex) when (string.Equals(ex.Message, "Horse is already registered.", StringComparison.Ordinal))
                        {
                            return Results.Conflict(new[] { ex.Message });
                        }
                    })
                    .WithName("RegisterHorse")
                    .WithTags("Horse API")
                    .Produces<RegisterHorseResponse>(StatusCodes.Status201Created)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
