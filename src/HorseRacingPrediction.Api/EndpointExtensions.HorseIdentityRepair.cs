using EventFlow.EntityFramework;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api;

public static partial class EndpointExtensions
{
    private const string HorseIdentityRepairId = "20260913-jra-horse-identity-repair";

    private static void MapHorseIdentityRepairEndpoints(RouteGroupBuilder group)
    {
        MapSubjectIdentificationRepairEndpoints(group);
        group.MapGet("/admin/repairs/20260913-jra-horse-identity",
            async (IDbContextProvider<EventStoreDbContext> provider, CollectionPlatformStore collectionStore,
                CancellationToken token) =>
            {
                using var db = provider.CreateContext();
                return Results.Ok(await BuildHorseIdentityRepairPreviewAsync(db, collectionStore, token).ConfigureAwait(false));
            });

        group.MapPost("/admin/repairs/20260913-jra-horse-identity/apply",
            async (ApplyHorseIdentityRepairRequest request,
                IDbContextProvider<EventStoreDbContext> provider, CollectionPlatformStore collectionStore,
                CancellationToken token) =>
            {
                if (request.CandidateIds is null || request.CandidateIds.Count == 0)
                    return Results.BadRequest(new[] { "dry-run manifestのCandidateIdを指定してください。" });
                if (request.CandidateIds.Count != request.CandidateIds.Distinct(StringComparer.Ordinal).Count())
                    return Results.BadRequest(new[] { "CandidateIdが重複しています。" });

                using var db = provider.CreateContext();
                await using var transaction = await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
                var requestedCandidates = await db.HorseIdentityRepairCandidates
                    .Where(x => x.RepairId == HorseIdentityRepairId && request.CandidateIds.Contains(x.CandidateId))
                    .ToListAsync(token).ConfigureAwait(false);
                if (requestedCandidates.Count != request.CandidateIds.Count)
                    return Results.Conflict(new[] { "manifestに存在しないCandidateIdがあります。dry-runを再実行してください。" });

                var completed = requestedCandidates.Where(x => x.AppliedAt != null).ToArray();
                foreach (var candidate in completed)
                {
                    var redirect = await db.HorseIdentityRepairRedirects.AsNoTracking().SingleOrDefaultAsync(
                        x => x.SourceHorseId == candidate.SourceHorseId, token).ConfigureAwait(false);
                    if (redirect?.TargetHorseId != candidate.TargetHorseId || redirect.RepairId != HorseIdentityRepairId)
                        return Results.Conflict(new[] { $"{candidate.CandidateId}の完了記録とredirectが整合しません。" });
                }

                var pendingIds = requestedCandidates.Where(x => x.AppliedAt == null)
                    .Select(x => x.CandidateId).ToHashSet(StringComparer.Ordinal);
                var preview = await BuildHorseIdentityRepairPreviewAsync(db, collectionStore, token).ConfigureAwait(false);
                var selected = preview.Candidates.Where(x => pendingIds.Contains(x.CandidateId)).ToArray();
                if (selected.Length != pendingIds.Count)
                    return Results.Conflict(new[] { "未処理CandidateIdをdry-run manifestで再検証できません。" });
                var blocked = selected.Where(x => !x.SafeToApply).ToArray();
                if (blocked.Length > 0)
                    return Results.Conflict(blocked.Select(x => $"{x.CandidateId}: {x.BlockingReason}").ToArray());
                var conflictingSources = selected.GroupBy(x => x.SourceHorseId, StringComparer.Ordinal)
                    .Where(group => group.Select(x => x.TargetHorseId).Distinct(StringComparer.Ordinal).Count() > 1)
                    .Select(group => group.Key).ToArray();
                if (conflictingSources.Length > 0)
                    return Results.Conflict(conflictingSources.Select(x =>
                        $"{x}に複数の統合先候補があります。候補を個別に再確認してください。").ToArray());

                var applied = 0;
                foreach (var sourceGroup in selected.GroupBy(x => x.SourceHorseId, StringComparer.Ordinal))
                {
                    var targetHorseId = sourceGroup.First().TargetHorseId;
                    var redirect = await db.HorseIdentityRepairRedirects.SingleOrDefaultAsync(
                        x => x.SourceHorseId == sourceGroup.Key, token).ConfigureAwait(false);
                    if (redirect is null)
                    {
                        db.HorseIdentityRepairRedirects.Add(new HorseIdentityRepairRedirectReadModel
                        {
                            SourceHorseId = sourceGroup.Key,
                            TargetHorseId = targetHorseId,
                            RepairId = HorseIdentityRepairId,
                            JraIdentity = sourceGroup.First().JraIdentity,
                            CreatedAt = HorseRacingPrediction.Contracts.Time.JstTime.Now(),
                        });
                    }
                    else if (!string.Equals(redirect.TargetHorseId, targetHorseId, StringComparison.Ordinal))
                    {
                        return Results.Conflict(new[] { $"{sourceGroup.Key}には異なるredirect先があります。" });
                    }
                    foreach (var item in sourceGroup)
                    {
                        var candidate = await db.HorseIdentityRepairCandidates.SingleAsync(
                            x => x.CandidateId == item.CandidateId, token).ConfigureAwait(false);
                        candidate.AppliedAt ??= HorseRacingPrediction.Contracts.Time.JstTime.Now();
                        applied++;
                    }
                }
                await db.SaveChangesAsync(token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
                var disabled = 0;
                var running = 0;
                foreach (var candidate in requestedCandidates)
                {
                    var suppression = await collectionStore.SuppressResourceAsync(
                        new ResourceKey(ResourceType.Horse, "JRA", candidate.SourceHorseId),
                        "JRA競走馬識別子の不具合修復により統合元データを削除済みです。",
                        HorseIdentityRepairId, HorseRacingPrediction.Contracts.Time.JstTime.Now(), token)
                        .ConfigureAwait(false);
                    disabled += suppression.CancelledTasks;
                    running += suppression.RunningCancellationRequests;
                }
                return Results.Ok(new ApplyHorseIdentityRepairResponse(HorseIdentityRepairId, applied,
                    completed.Length, disabled, running));
            });
    }

    private static async Task<HorseIdentityRepairPreviewResponse> BuildHorseIdentityRepairPreviewAsync(
        EventStoreDbContext db, CollectionPlatformStore collectionStore, CancellationToken token)
    {
        var candidates = await db.HorseIdentityRepairCandidates.AsNoTracking()
            .Where(x => x.RepairId == HorseIdentityRepairId && x.AppliedAt == null)
            .OrderBy(x => x.CandidateId).ToListAsync(token).ConfigureAwait(false);
        var horses = await db.Horses.AsNoTracking().ToDictionaryAsync(x => x.HorseId, token).ConfigureAwait(false);
        var races = await db.RacePredictionContexts.AsNoTracking().ToListAsync(token).ConfigureAwait(false);
        var redirects = await db.HorseIdentityRepairRedirects.AsNoTracking()
            .ToDictionaryAsync(x => x.SourceHorseId, token).ConfigureAwait(false);
        var result = new List<HorseIdentityRepairCandidateResponse>();
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
                new ResourceKey(ResourceType.Horse, "JRA", candidate.SourceHorseId), token).ConfigureAwait(false);
            var raceName = races.SingleOrDefault(x => x.RaceId == candidate.RaceId)?.RaceName;
            result.Add(new(candidate.CandidateId, candidate.SourceHorseId, candidate.TargetHorseId,
                candidate.JraIdentity, candidate.RaceId, candidate.EntryId, blocked is null, blocked,
                source?.RegisteredName, target?.RegisteredName, raceName, collectionTasks.TotalTasks));
        }
        return new(HorseIdentityRepairId, result);
    }
}
