using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Repairs;

namespace HorseRacingPrediction.Api.Endpoints.Repairs;

internal static class HorseIdentityRepairService
{
    internal const string HorseIdentityRepairId = "20260913-jra-horse-identity-repair";


    internal static async Task<HorseIdentityRepairPreviewDto> BuildHorseIdentityRepairPreviewAsync(
        EventStoreDbContext db, CollectionPlatformStore collectionStore, CancellationToken token)
    {
        var candidates = await db.HorseIdentityRepairCandidates.AsNoTracking()
            .Where(x => x.RepairId == HorseIdentityRepairId && x.AppliedAt == null)
            .OrderBy(x => x.CandidateId).ToListAsync(token).ConfigureAwait(false);
        var horses = await db.Horses.AsNoTracking().ToDictionaryAsync(x => x.HorseId, token).ConfigureAwait(false);
        var races = await db.RacePredictionContexts.AsNoTracking().ToListAsync(token).ConfigureAwait(false);
        var redirects = await db.HorseIdentityRepairRedirects.AsNoTracking()
            .ToDictionaryAsync(x => x.SourceHorseId, token).ConfigureAwait(false);
        var result = new List<HorseIdentityRepairCandidateDto>();
        foreach (var candidate in candidates)
        {
            string? blocked = null;
            horses.TryGetValue(candidate.SourceHorseId, out var source);
            horses.TryGetValue(candidate.TargetHorseId, out var target);
            if (source is null || target is null)
                blocked = "sourceまたはtarget Horseが存在しません。";
            else if (redirects.TryGetValue(candidate.SourceHorseId, out var redirect)
                     && !string.Equals(redirect.TargetHorseId, candidate.TargetHorseId, StringComparison.Ordinal))
                blocked = "source Horseに異なるredirect先があります。";
            else if (!string.Equals(candidate.TargetHorseId,
                         DeterministicIdGenerator.BuildHorseId(target.RegisteredName,
                             JraSourceIdentity.NormalizeHorseUrl(candidate.JraIdentity)?.ToString()
                             ?? $"/JRADB/accessU.html?CNAME={candidate.JraIdentity}"), StringComparison.Ordinal))
                blocked = "target Horse IDとJRA identityが整合しません。";
            else if (races.SelectMany(x => x.Entries).Any(x => x.HorseId == candidate.SourceHorseId))
                blocked = "source Horseを参照するRaceEntryが残っています。対象RaceCardを先に再取得してください。";
            else
            {
                var evidence = races.SingleOrDefault(x => x.RaceId == candidate.RaceId)?.Entries
                    .SingleOrDefault(x => x.EntryId == candidate.EntryId);
                if (evidence?.HorseId != candidate.TargetHorseId)
                    blocked = "根拠RaceEntryがtarget Horseを参照していません。";
                else if (!string.Equals(source.NormalizedName, target.NormalizedName, StringComparison.Ordinal))
                    blocked = "sourceとtargetの正規化名が一致しません。";
            }
            var collectionTasks = await collectionStore.GetResourceSuppressionPreviewAsync(
                new ResourceKey(CollectionResourceType.Horse, "JRA", candidate.SourceHorseId), token).ConfigureAwait(false);
            var raceName = races.SingleOrDefault(x => x.RaceId == candidate.RaceId)?.RaceName;
            result.Add(new(candidate.CandidateId, candidate.SourceHorseId, candidate.TargetHorseId,
                candidate.JraIdentity, candidate.RaceId, candidate.EntryId, blocked is null, blocked,
                source?.RegisteredName, target?.RegisteredName, raceName, collectionTasks.TotalTasks));
        }
        return new(HorseIdentityRepairId, result);
    }
}
