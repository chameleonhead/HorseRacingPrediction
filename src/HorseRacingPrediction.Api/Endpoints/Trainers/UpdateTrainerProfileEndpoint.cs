using EventFlow;
using HorseRacingPrediction.Application.Commands.Trainers;
using HorseRacingPrediction.Domain.Trainers;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Trainers;

namespace HorseRacingPrediction.Api.Endpoints.Trainers;

internal static class UpdateTrainerProfileEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/trainers/{trainerId}",
                    [SwaggerOperation(Summary = "Update trainer profile", Description = "Updates profile information of an existing trainer")]
        async (string trainerId, UpdateTrainerProfileRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        var command = new UpdateTrainerProfileCommand(
                            new TrainerId(trainerId),
                            request.DisplayName,
                            request.NormalizedName,
                            request.AffiliationCode);

                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        return result.IsSuccess
                            ? Results.Ok()
                            : Results.BadRequest(new[] { "Command execution failed." });
                    })
                    .WithName("UpdateTrainerProfile")
                    .WithTags("Trainer API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
