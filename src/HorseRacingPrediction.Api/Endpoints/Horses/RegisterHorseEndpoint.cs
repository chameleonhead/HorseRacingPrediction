using EventFlow;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Domain.Horses;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Horses;

internal static class RegisterHorseEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/horses",
                    [SwaggerOperation(Summary = "Register horse", Description = "Registers a new horse")]
        async (RegisterHorseRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        try
                        {
                            var horseId = string.IsNullOrWhiteSpace(request.HorseId) ? HorseId.New : new HorseId(request.HorseId);
                            var command = new RegisterHorseCommand(
                                horseId,
                                request.RegisteredName,
                                request.NormalizedName,
                                request.SexCode,
                                request.BirthDate,
                                request.OwnerName, request.BreederName, request.SireName, request.DamName,
                                request.DamsireName, request.CoatColor);

                            var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                            return result.IsSuccess
                                ? Results.Created($"/api/horses/{horseId.Value}", new { HorseId = horseId.Value })
                                : Results.BadRequest(new[] { "Command execution failed." });
                        }
                        catch (InvalidOperationException ex) when (string.Equals(ex.Message, "Horse is already registered.", StringComparison.Ordinal))
                        {
                            return Results.Conflict(new[] { ex.Message });
                        }
                    })
                    .WithName("RegisterHorse")
                    .WithTags("Horse API")
                    .Produces(StatusCodes.Status201Created)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
