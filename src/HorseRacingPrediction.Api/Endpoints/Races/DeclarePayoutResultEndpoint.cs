using EventFlow;
using ApiContracts = HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Domain.Races;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static class DeclarePayoutResultEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/races/{raceId}/payout",
                    [SwaggerOperation(Summary = "Declare payout result", Description = "Declares payout information for win/place/quinella/exacta/trifecta bets")]
        async (string raceId, HorseRacingPrediction.Contracts.Races.DeclarePayoutResultRequest request, ICommandBus commandBus, CancellationToken cancellationToken) =>
                    {
                        try
                        {
                            static IReadOnlyList<PayoutEntry>? ToPayoutEntries(IReadOnlyList<HorseRacingPrediction.Contracts.Races.PayoutEntryDto>? dtos) =>
                                dtos?.Select(d => new PayoutEntry(d.Combination, d.Amount)).ToList();

                            var command = new DeclarePayoutResultCommand(
                                new RaceId(raceId),
                                request.DeclaredAt,
                                ToPayoutEntries(request.WinPayouts),
                                ToPayoutEntries(request.PlacePayouts),
                                ToPayoutEntries(request.QuinellaPayouts),
                                ToPayoutEntries(request.ExactaPayouts),
                                ToPayoutEntries(request.TrifectaPayouts));

                            var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                            return result.IsSuccess
                                ? Results.Ok()
                                : Results.BadRequest(new[] { "Command execution failed." });
                        }
                        catch (InvalidOperationException ex)
                        {
                            return Results.Conflict(new[] { ex.Message });
                        }
                    })
                    .WithName("DeclarePayoutResult")
                    .WithTags("Race API")
                    .Produces(StatusCodes.Status200OK)
                    .Produces<IEnumerable<string>>(StatusCodes.Status409Conflict)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
