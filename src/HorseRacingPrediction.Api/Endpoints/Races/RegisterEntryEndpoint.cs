using EventFlow;
using EventFlow.EntityFramework;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Races;

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
                        var entry = request?.Entry;
                        if (entry is null) return Results.BadRequest(new[] { "Entry is required." });
                        if (string.IsNullOrWhiteSpace(entry.HorseId) || entry.HorseNumber is <= 0
                            || entry.GateNumber is < 1 or > 8
                            || (entry.ParticipationStatus is { } participation && !Enum.IsDefined(participation)))
                            return Results.BadRequest(new[] { "A HorseId and valid known horse/frame numbers are required." });
                        var entryId = DeterministicIdGenerator.BuildRaceEntryId(raceId, entry.HorseId);
                        if (!string.IsNullOrWhiteSpace(entry.EntryId) && entry.EntryId != entryId)
                            return Results.BadRequest(new[] { "EntryId must identify the race and horse, not the horse number." });
                        using (var readContext = dbContextProvider.CreateContext())
                        {
                            var currentRace = await readContext.RacePredictionContexts.AsNoTracking()
                                .SingleOrDefaultAsync(x => x.RaceId == raceId, cancellationToken).ConfigureAwait(false);
                            if (entry.HorseNumber is { } number && currentRace?.Entries.Any(x => x.HorseNumber == number && x.HorseId != entry.HorseId) == true)
                                return Results.Conflict(new[] { "HorseNumber is already assigned to another horse; submit the complete card for reassignment." });
                            var old = currentRace?.Entries.FirstOrDefault(x => x.HorseId == entry.HorseId);
                            entry = entry with { HorseNumber = entry.HorseNumber ?? old?.HorseNumber, GateNumber = entry.GateNumber ?? old?.GateNumber };
                        }
                        await EnsureRelatedSubjectsAsync(entry, commandBus, dbContextProvider, cancellationToken).ConfigureAwait(false);

                        var command = new RegisterEntryCommand(
                            new RaceId(raceId),
                            entryId,
                            entry.HorseId,
                            entry.HorseNumber,
                            entry.JockeyId,
                            entry.TrainerId,
                            entry.GateNumber,
                            entry.AssignedWeight,
                            entry.SexCode,
                            entry.Age,
                            entry.DeclaredWeight,
                            entry.DeclaredWeightDiff,
                            entry.RunningStyleCode,
                            entry.OwnerName, (Domain.Races.RaceEntryParticipationStatus?)entry.ParticipationStatus);

                        var result = await commandBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
                        if (!result.IsSuccess) return Results.BadRequest(new[] { "Command execution failed." });
                        return Results.Created($"/api/races/{raceId}/entries/{entryId}", new RegisterEntryResponse(raceId, entryId));
                    })
                    .WithName("RegisterEntry")
                    .WithTags("Race API")
                    .Produces<RegisterEntryResponse>(StatusCodes.Status201Created)
                    .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status401Unauthorized);
    }
}
