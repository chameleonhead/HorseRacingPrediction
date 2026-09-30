using EventFlow.EntityFramework;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

using HorseRacingPrediction.Contracts.Common;

namespace HorseRacingPrediction.Api.Endpoints.Trainers;

using static HorseRacingPrediction.Api.Endpoints.Shared.EndpointQueryUtilities;


internal static class GetTrainerParticipationsEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/trainers/{trainerId}/participations",
                    [SwaggerOperation(Summary = "Get trainer participation history", Description = "Returns races and related horses, jockeys and owners for a trainer")]
        async (string trainerId, int? take, int? skip, IDbContextProvider<EventStoreDbContext> dbContextProvider, CancellationToken cancellationToken) =>
                    {
                        using var dbContext = dbContextProvider.CreateContext();
                        var trainerExists = await dbContext.Trainers.AsNoTracking().AnyAsync(x => x.TrainerId == trainerId, cancellationToken).ConfigureAwait(false);
                        if (!trainerExists) return Results.NotFound();

                        var contexts = await dbContext.RacePredictionContexts.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
                        var matching = contexts
                            .SelectMany(race => race.Entries.Where(entry => entry.TrainerId == trainerId).Select(entry => (race, entry)))
                            .ToList();
                        var raceIds = matching.Select(x => x.race.RaceId).Distinct(StringComparer.Ordinal).ToList();
                        var results = await dbContext.RaceResults.AsNoTracking().Where(x => raceIds.Contains(x.RaceId)).ToListAsync(cancellationToken).ConfigureAwait(false);
                        var resultByRace = results.ToDictionary(x => x.RaceId, StringComparer.Ordinal);
                        var horses = await dbContext.Horses.AsNoTracking().ToDictionaryAsync(x => x.HorseId, x => x.RegisteredName, cancellationToken).ConfigureAwait(false);
                        var jockeys = await dbContext.Jockeys.AsNoTracking().ToDictionaryAsync(x => x.JockeyId, x => x.DisplayName, cancellationToken).ConfigureAwait(false);
                        var trainer = await dbContext.Trainers.AsNoTracking().SingleAsync(x => x.TrainerId == trainerId, cancellationToken).ConfigureAwait(false);

                        var allEntries = matching.Select(x =>
                        {
                            resultByRace.TryGetValue(x.race.RaceId, out var raceResult);
                            var entryResult = raceResult?.EntryResults.FirstOrDefault(y => y.EntryId == x.entry.EntryId);
                            return new ParticipationHistoryEntryDto(
                                x.race.RaceId, x.race.RaceDate, x.race.RacecourseCode, x.race.RaceNumber, x.race.RaceName,
                                x.entry.HorseId, horses.GetValueOrDefault(x.entry.HorseId, x.entry.HorseId),
                                x.entry.JockeyId, x.entry.JockeyId is null ? null : jockeys.GetValueOrDefault(x.entry.JockeyId, x.entry.JockeyId),
                                trainerId, trainer.DisplayName, x.entry.OwnerName, entryResult?.FinishPosition, entryResult?.PrizeMoney);
                        }).OrderByDescending(x => x.RaceDate).ThenByDescending(x => x.RaceNumber).ToList();

                        var limit = Math.Max(take.GetValueOrDefault(10), 1);
                        var offset = Math.Max(skip.GetValueOrDefault(0), 0);
                        var entries = allEntries.Skip(offset).Take(limit).ToList();
                        var hasMore = offset + entries.Count < allEntries.Count;

                        var relationshipEntries = EntriesInLastThreeYears(allEntries);
                        var relationships = relationshipEntries.GroupBy(x => (x.HorseId, x.HorseName)).Select(x =>
                                new RelationshipSummaryDto("Horse", x.Key.HorseId, x.Key.HorseName, "管理した馬", x.Count(), x.Max(y => y.RaceDate), x.Sum(y => y.PrizeMoney ?? 0m), x.Count(y => y.FinishPosition == 1)))
                            .Concat(relationshipEntries.Where(x => x.JockeyId is not null).GroupBy(x => (x.JockeyId, x.JockeyName)).Select(x =>
                                new RelationshipSummaryDto("Jockey", x.Key.JockeyId!, x.Key.JockeyName!, "騎乗した騎手", x.Count(), x.Max(y => y.RaceDate))))
                            .OrderByDescending(x => x.ParticipationCount).ToList();

                        return Results.Ok(new ParticipationHistoryDto("Trainer", trainerId, entries, relationships, hasMore));
                    })
                    .WithName("GetTrainerParticipations")
                    .WithTags("Trainer API")
                    .Produces<ParticipationHistoryDto>(StatusCodes.Status200OK)
                    .Produces(StatusCodes.Status404NotFound);
    }
}
