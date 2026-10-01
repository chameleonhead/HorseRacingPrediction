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
                        if (request.Jockey is null) return Results.BadRequest(new[] { "Jockey payload is required." });
                        var jockey = request.Jockey;
                        try
                        {
                            var jockeyId = string.IsNullOrWhiteSpace(jockey.JockeyId) ? JockeyId.New : new JockeyId(jockey.JockeyId);
                            var command = new RegisterJockeyCommand(
                                jockeyId,
                                jockey.DisplayName,
                                jockey.NormalizedName,
                                jockey.AffiliationCode);

                            var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                            return result.IsSuccess
                                ? Results.Created($"/api/jockeys/{jockeyId.Value}", new RegisterJockeyResponse(jockeyId.Value))
                                : Results.BadRequest(new[] { "Command execution failed." });
                        }
                        catch (InvalidOperationException ex) when (string.Equals(ex.Message, "Jockey is already registered.", StringComparison.Ordinal))
                        {
                            return Results.Conflict(new[] { ex.Message });
                        }
                    })
                    .WithName("RegisterJockey")
                    .WithTags("Jockey API")
                    .Produces<RegisterJockeyResponse>(StatusCodes.Status201Created)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
