using EventFlow;
using HorseRacingPrediction.Application.Commands.Trainers;
using HorseRacingPrediction.Domain.Trainers;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Trainers;

namespace HorseRacingPrediction.Api.Endpoints.Trainers;

internal static class RegisterTrainerEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/trainers",
                    [SwaggerOperation(Summary = "Register trainer", Description = "Registers a new trainer")]
        async (RegisterTrainerRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        try
                        {
                            var trainerId = string.IsNullOrWhiteSpace(request.TrainerId) ? TrainerId.New : new TrainerId(request.TrainerId);
                            var command = new RegisterTrainerCommand(
                                trainerId,
                                request.DisplayName,
                                request.NormalizedName,
                                request.AffiliationCode);

                            var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                            return result.IsSuccess
                                ? Results.Created($"/api/trainers/{trainerId.Value}", new { TrainerId = trainerId.Value })
                                : Results.BadRequest(new[] { "Command execution failed." });
                        }
                        catch (InvalidOperationException ex) when (string.Equals(ex.Message, "Trainer is already registered.", StringComparison.Ordinal))
                        {
                            return Results.Conflict(new[] { ex.Message });
                        }
                    })
                    .WithName("RegisterTrainer")
                    .WithTags("Trainer API")
                    .Produces(StatusCodes.Status201Created)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
