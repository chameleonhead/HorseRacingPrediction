using EventFlow;
using HorseRacingPrediction.Application.Commands.Trainers;
using HorseRacingPrediction.Domain.Trainers;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Trainers;

namespace HorseRacingPrediction.Api.Endpoints.Trainers;

internal static class CorrectTrainerDataEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/trainers/{trainerId}",
                    [SwaggerOperation(Summary = "Correct trainer data", Description = "Corrects trainer master data with an optional audit reason")]
        async (string trainerId, CorrectTrainerDataRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        var command = new CorrectTrainerDataCommand(
                            new TrainerId(trainerId),
                            request.DisplayName,
                            request.NormalizedName,
                            request.AffiliationCode,
                            request.Reason);

                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        return result.IsSuccess
                            ? Results.Ok()
                            : Results.BadRequest(new[] { "Command execution failed." });
                    })
                    .WithName("CorrectTrainerData")
                    .WithTags("Trainer API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
