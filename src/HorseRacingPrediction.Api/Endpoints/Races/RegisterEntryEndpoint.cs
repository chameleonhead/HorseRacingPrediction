using EventFlow;
using EventFlow.EntityFramework;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace HorseRacingPrediction.Api.Endpoints.Races;

using static HorseRacingPrediction.Api.Endpoints.Races.RaceEndpointMappings;


internal static class RegisterEntryEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/races/{raceId}/entries",
                    [SwaggerOperation(Summary = "Register entry", Description = "Registers a horse entry for a race after card publication")]
        async (string raceId, RegisterEntryRequest request, ICommandBus commandBus, IDbContextProvider<EventStoreDbContext> dbContextProvider, CancellationToken cancellationToken) =>
                    {
                        if (string.IsNullOrWhiteSpace(request.HorseId) || request.HorseNumber is <= 0
                            || request.GateNumber is < 1 or > 8
                            || (request.ParticipationStatus is { } participation && !Enum.IsDefined(participation)))
                            return Results.BadRequest(new[] { "A HorseId and valid known horse/frame numbers are required." });
                        var entryId = DeterministicIdGenerator.BuildRaceEntryId(raceId, request.HorseId);
                        if (!string.IsNullOrWhiteSpace(request.EntryId) && request.EntryId != entryId)
                            return Results.BadRequest(new[] { "EntryId must identify the race and horse, not the horse number." });
                        using (var readContext = dbContextProvider.CreateContext())
                        {
                            var currentRace = await readContext.RacePredictionContexts.AsNoTracking()
                                .SingleOrDefaultAsync(x => x.RaceId == raceId, cancellationToken).ConfigureAwait(false);
                            if (request.HorseNumber is { } number && currentRace?.Entries.Any(x => x.HorseNumber == number && x.HorseId != request.HorseId) == true)
                                return Results.Conflict(new[] { "HorseNumber is already assigned to another horse; submit the complete card for reassignment." });
                            var old = currentRace?.Entries.FirstOrDefault(x => x.HorseId == request.HorseId);
                            request = request with { HorseNumber = request.HorseNumber ?? old?.HorseNumber, GateNumber = request.GateNumber ?? old?.GateNumber };
                        }
                        await EnsureRelatedSubjectsAsync(request, commandBus, dbContextProvider, cancellationToken).ConfigureAwait(false);

                        var command = new RegisterEntryCommand(
                            new RaceId(raceId),
                            entryId,
                            request.HorseId,
                            request.HorseNumber,
                            request.JockeyId,
                            request.TrainerId,
                            request.GateNumber,
                            request.AssignedWeight,
                            request.SexCode,
                            request.Age,
                            request.DeclaredWeight,
                            request.DeclaredWeightDiff,
                            request.RunningStyleCode,
                            request.OwnerName, (Domain.Races.RaceEntryParticipationStatus?)request.ParticipationStatus);

                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        if (!result.IsSuccess) return Results.BadRequest(new[] { "Command execution failed." });
                        return Results.Created($"/api/races/{raceId}/entries/{entryId}", new { RaceId = raceId, EntryId = entryId });
                    })
                    .WithName("RegisterEntry")
                    .WithTags("Race API")
                    .Produces(StatusCodes.Status201Created)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
