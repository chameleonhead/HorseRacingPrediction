using EventFlow.EntityFramework;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.Api.Endpoints.Owners;

using static HorseRacingPrediction.Api.Endpoints.Shared.EndpointQueryUtilities;
using static HorseRacingPrediction.Api.Endpoints.Owners.OwnerEndpointService;


internal static class GetOwnerEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/owners/{ownerId}",
                    async (string ownerId, int? take, int? skip, IDbContextProvider<EventStoreDbContext> dbContextProvider, CancellationToken cancellationToken) =>
                    {
                        using var dbContext = dbContextProvider.CreateContext();
                        var owners = await BuildOwnersAsync(dbContext, cancellationToken).ConfigureAwait(false);
                        var owner = owners.SingleOrDefault(x => x.OwnerId == ownerId
                            || x.NameVariants.Any(name => OwnerIdentityContract.CreateLegacyId(name) == ownerId));
                        if (owner is null) return Results.NotFound();

                        var names = owner.NameVariants.ToHashSet(StringComparer.Ordinal);
                        var contexts = await dbContext.RacePredictionContexts.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
                        var matching = contexts.SelectMany(r => r.Entries.Where(e => e.OwnerName is not null && names.Contains(e.OwnerName)).Select(e => (race: r, entry: e))).ToList();
                        var raceIds = matching.Select(x => x.race.RaceId).Distinct().ToList();
                        var resultByRace = (await dbContext.RaceResults.AsNoTracking().Where(x => raceIds.Contains(x.RaceId)).ToListAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(x => x.RaceId);
                        var horses = await dbContext.Horses.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
                        var jockeys = await dbContext.Jockeys.AsNoTracking().ToDictionaryAsync(x => x.JockeyId, x => x.DisplayName, cancellationToken).ConfigureAwait(false);
                        var trainers = await dbContext.Trainers.AsNoTracking().ToDictionaryAsync(x => x.TrainerId, x => x.DisplayName, cancellationToken).ConfigureAwait(false);
                        var allEntries = matching.Select(x =>
                        {
                            resultByRace.TryGetValue(x.race.RaceId, out var result);
                            var entryResult = result?.EntryResults.FirstOrDefault(y => y.EntryId == x.entry.EntryId);
                            var horseName = horses.FirstOrDefault(y => y.HorseId == x.entry.HorseId)?.RegisteredName ?? x.entry.HorseId;
                            return new ParticipationHistoryEntryResponse(x.race.RaceId, x.race.RaceDate, x.race.RacecourseCode, x.race.RaceNumber, x.race.RaceName,
                                x.entry.HorseId, horseName, x.entry.JockeyId, x.entry.JockeyId is null ? null : jockeys.GetValueOrDefault(x.entry.JockeyId, x.entry.JockeyId),
                                x.entry.TrainerId, x.entry.TrainerId is null ? null : trainers.GetValueOrDefault(x.entry.TrainerId, x.entry.TrainerId), x.entry.OwnerName,
                                entryResult?.FinishPosition, entryResult?.PrizeMoney);
                        }).OrderByDescending(x => x.RaceDate).ToList();
                        var limit = Math.Max(take.GetValueOrDefault(10), 1);
                        var offset = Math.Max(skip.GetValueOrDefault(0), 0);
                        var entries = allEntries.Skip(offset).Take(limit).ToList();
                        var hasMoreParticipations = offset + entries.Count < allEntries.Count;
                        var currentHorses = horses.Where(x => x.OwnerName is not null && names.Contains(x.OwnerName))
                            .OrderByDescending(x => allEntries.Where(y => y.HorseId == x.HorseId).Select(y => y.RaceDate).DefaultIfEmpty().Max())
                            .ThenBy(x => x.RegisteredName)
                            .Select(x =>
                            new RelatedObjectResponse("Horse", x.HorseId, x.RegisteredName, allEntries.Count(y => y.HorseId == x.HorseId))).ToList();
                        var relatedTrainers = allEntries.Where(x => x.TrainerId is not null).GroupBy(x => (x.TrainerId, x.TrainerName)).Select(x =>
                            new RelatedObjectResponse("Trainer", x.Key.TrainerId!, x.Key.TrainerName!, x.Count())).OrderByDescending(x => x.RelationshipCount).ToList();
                        var mergeAudits = await dbContext.OwnerMergeAudits.AsNoTracking().Where(x => x.TargetOwnerId == ownerId)
                            .ToListAsync(cancellationToken).ConfigureAwait(false);
                        var mergeHistory = mergeAudits.OrderByDescending(x => x.CreatedAt).Select(x => new OwnerMergeAuditResponse(
                            x.SourceOwnerId, x.TargetOwnerId, x.SourceNames.Split('\n', StringSplitOptions.RemoveEmptyEntries), x.ActorId, x.Reason, x.CreatedAt)).ToList();
                        var ownerTopHorses = EntriesInLastThreeYears(allEntries)
                            .GroupBy(x => (x.HorseId, x.HorseName))
                            .Select(x => new RelationshipSummaryResponse("Horse", x.Key.HorseId, x.Key.HorseName, "所有した馬", x.Count(), x.Max(y => y.RaceDate), x.Sum(y => y.PrizeMoney ?? 0m), x.Count(y => y.FinishPosition == 1)))
                            .OrderByDescending(x => x.PrizeMoneyTotal).ThenByDescending(x => x.ParticipationCount).Take(5).ToList();
                        return Results.Ok(new OwnerDetailResponse(owner, currentHorses, relatedTrainers, entries, mergeHistory, hasMoreParticipations, ownerTopHorses));
                    })
                    .WithName("GetOwner")
                    .WithTags("Owner API")
                    .Produces<OwnerDetailResponse>()
                    .Produces(StatusCodes.Status404NotFound);
    }
}
