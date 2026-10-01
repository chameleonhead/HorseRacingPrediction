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
                        if (request.Trainer is null) return Results.BadRequest(new[] { "Trainer payload is required." });
                        var trainer = request.Trainer;
                        try
                        {
                            var trainerId = string.IsNullOrWhiteSpace(trainer.TrainerId) ? TrainerId.New : new TrainerId(trainer.TrainerId);
                            var command = new RegisterTrainerCommand(
                                trainerId,
                                trainer.DisplayName,
                                trainer.NormalizedName,
                                trainer.AffiliationCode);

                            var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                            return result.IsSuccess
                                ? Results.Created($"/api/trainers/{trainerId.Value}", new RegisterTrainerResponse(trainerId.Value))
                                : Results.BadRequest(new[] { "Command execution failed." });
                        }
                        catch (InvalidOperationException ex) when (string.Equals(ex.Message, "Trainer is already registered.", StringComparison.Ordinal))
                        {
                            return Results.Conflict(new[] { ex.Message });
                        }
                    })
                    .WithName("RegisterTrainer")
                    .WithTags("Trainer API")
                    .Produces<RegisterTrainerResponse>(StatusCodes.Status201Created)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
