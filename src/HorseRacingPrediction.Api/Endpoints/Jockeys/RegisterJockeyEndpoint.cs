using EventFlow;
using HorseRacingPrediction.Application.Commands.Jockeys;
using HorseRacingPrediction.Domain.Jockeys;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Jockeys;

namespace HorseRacingPrediction.Api.Endpoints.Jockeys;

internal static class RegisterJockeyEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/jockeys",
                    [SwaggerOperation(Summary = "Register jockey", Description = "Registers a new jockey")]
        async (RegisterJockeyRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        try
                        {
                            var jockeyId = string.IsNullOrWhiteSpace(request.JockeyId) ? JockeyId.New : new JockeyId(request.JockeyId);
                            var command = new RegisterJockeyCommand(
                                jockeyId,
                                request.DisplayName,
                                request.NormalizedName,
                                request.AffiliationCode);

                            var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                            return result.IsSuccess
                                ? Results.Created($"/api/jockeys/{jockeyId.Value}", new { JockeyId = jockeyId.Value })
                                : Results.BadRequest(new[] { "Command execution failed." });
                        }
                        catch (InvalidOperationException ex) when (string.Equals(ex.Message, "Jockey is already registered.", StringComparison.Ordinal))
                        {
                            return Results.Conflict(new[] { ex.Message });
                        }
                    })
                    .WithName("RegisterJockey")
                    .WithTags("Jockey API")
                    .Produces(StatusCodes.Status201Created)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
