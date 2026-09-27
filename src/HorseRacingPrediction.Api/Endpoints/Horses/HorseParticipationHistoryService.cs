using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.Api.Endpoints.Horses;

internal static class HorseParticipationHistoryService
{
    internal static async Task<ParticipationHistoryResponse> BuildParticipationHistoryAsync(
        string subjectType,
        string subjectId,
        EventStoreDbContext dbContext,
        int? take,
        int? skip,
        CancellationToken cancellationToken)
    {
        var contexts = await dbContext.RacePredictionContexts.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var matching = contexts.SelectMany(race => race.Entries
            .Where(entry => subjectType == "Horse" ? entry.HorseId == subjectId : entry.JockeyId == subjectId)
            .Select(entry => (race, entry))).ToList();
        var raceIds = matching.Select(x => x.race.RaceId).Distinct(StringComparer.Ordinal).ToList();
        var resultByRace = (await dbContext.RaceResults.AsNoTracking().Where(x => raceIds.Contains(x.RaceId)).ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(x => x.RaceId, StringComparer.Ordinal);
        var horses = await dbContext.Horses.AsNoTracking().ToDictionaryAsync(x => x.HorseId, x => x.RegisteredName, cancellationToken).ConfigureAwait(false);
        var jockeys = await dbContext.Jockeys.AsNoTracking().ToDictionaryAsync(x => x.JockeyId, x => x.DisplayName, cancellationToken).ConfigureAwait(false);
        var trainers = await dbContext.Trainers.AsNoTracking().ToDictionaryAsync(x => x.TrainerId, x => x.DisplayName, cancellationToken).ConfigureAwait(false);

        var allEntries = matching.Select(x =>
        {
            resultByRace.TryGetValue(x.race.RaceId, out var raceResult);
            var entryResult = raceResult?.EntryResults.FirstOrDefault(y => y.EntryId == x.entry.EntryId);
            return new ParticipationHistoryEntryResponse(
                x.race.RaceId, x.race.RaceDate, x.race.RacecourseCode, x.race.RaceNumber, x.race.RaceName,
                x.entry.HorseId, horses.GetValueOrDefault(x.entry.HorseId, x.entry.HorseId),
                x.entry.JockeyId, x.entry.JockeyId is null ? null : jockeys.GetValueOrDefault(x.entry.JockeyId, x.entry.JockeyId),
                x.entry.TrainerId, x.entry.TrainerId is null ? null : trainers.GetValueOrDefault(x.entry.TrainerId, x.entry.TrainerId),
                x.entry.OwnerName, entryResult?.FinishPosition, entryResult?.PrizeMoney);
        }).OrderByDescending(x => x.RaceDate).ThenByDescending(x => x.RaceNumber).ToList();

        var limit = Math.Max(take.GetValueOrDefault(subjectType == "Horse" ? 100 : 10), 1);
        var offset = Math.Max(skip.GetValueOrDefault(0), 0);
        var entries = allEntries.Skip(offset).Take(limit).ToList();
        var hasMore = offset + entries.Count < allEntries.Count;

        var relationshipEntries = subjectType == "Horse" ? allEntries : EntriesInLastThreeYears(allEntries);
        IEnumerable<RelationshipSummaryResponse> relationships = subjectType == "Horse"
            ? allEntries.Where(x => x.JockeyId is not null).GroupBy(x => (x.JockeyId, x.JockeyName)).Select(x =>
                    new RelationshipSummaryResponse("Jockey", x.Key.JockeyId!, x.Key.JockeyName!, "騎乗した騎手", x.Count(), x.Max(y => y.RaceDate)))
                .Concat(allEntries.Where(x => x.TrainerId is not null).GroupBy(x => (x.TrainerId, x.TrainerName)).Select(x =>
                    new RelationshipSummaryResponse("Trainer", x.Key.TrainerId!, x.Key.TrainerName!, "レース時点の調教師", x.Count(), x.Max(y => y.RaceDate))))
            : relationshipEntries.GroupBy(x => (x.HorseId, x.HorseName)).Select(x =>
                    new RelationshipSummaryResponse("Horse", x.Key.HorseId, x.Key.HorseName, "騎乗した馬", x.Count(), x.Max(y => y.RaceDate), x.Sum(y => y.PrizeMoney ?? 0m), x.Count(y => y.FinishPosition == 1)))
                .Concat(relationshipEntries.Where(x => x.TrainerId is not null).GroupBy(x => (x.TrainerId, x.TrainerName)).Select(x =>
                    new RelationshipSummaryResponse("Trainer", x.Key.TrainerId!, x.Key.TrainerName!, "同じ出走の調教師", x.Count(), x.Max(y => y.RaceDate))));

        return new ParticipationHistoryResponse(subjectType, subjectId, entries, relationships.OrderByDescending(x => x.ParticipationCount).ToList(), hasMore);
    }

    internal static IReadOnlyList<ParticipationHistoryEntryResponse> EntriesInLastThreeYears(IReadOnlyList<ParticipationHistoryEntryResponse> entries)
    {
        var latestDate = entries.Where(x => x.RaceDate.HasValue).Select(x => x.RaceDate!.Value).DefaultIfEmpty().Max();
        if (latestDate == default) return entries;
        var from = latestDate.AddYears(-3);
        return entries.Where(x => !x.RaceDate.HasValue || x.RaceDate.Value >= from).ToList();
    }
}
