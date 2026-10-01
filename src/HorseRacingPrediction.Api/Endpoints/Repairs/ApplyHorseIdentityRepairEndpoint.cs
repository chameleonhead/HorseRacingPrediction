using EventFlow.EntityFramework;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Common.Time;
using HorseRacingPrediction.Contracts.Repairs;

namespace HorseRacingPrediction.Api.Endpoints.Repairs;

using static HorseRacingPrediction.Api.Endpoints.Repairs.HorseIdentityRepairService;


internal static class ApplyHorseIdentityRepairEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
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
                                    CreatedAt = HorseRacingPrediction.Contracts.Common.Time.JstTime.Now(),
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
                                candidate.AppliedAt ??= HorseRacingPrediction.Contracts.Common.Time.JstTime.Now();
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
                                new ResourceKey(CollectionResourceType.Horse, "JRA", candidate.SourceHorseId),
                                "JRA競走馬識別子の不具合修復により統合元データを削除済みです。",
                                HorseIdentityRepairId, HorseRacingPrediction.Contracts.Common.Time.JstTime.Now(), token)
                                .ConfigureAwait(false);
                            disabled += suppression.CancelledTasks;
                            running += suppression.RunningCancellationRequests;
                        }
                        return Results.Ok(new ApplyHorseIdentityRepairResponse(new HorseIdentityRepairApplicationDto(
                            HorseIdentityRepairId, applied, completed.Length, disabled, running)));
                    })
                    .Produces<ApplyHorseIdentityRepairResponse>(StatusCodes.Status200OK)
                    .Produces(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status409Conflict);
    }
}
