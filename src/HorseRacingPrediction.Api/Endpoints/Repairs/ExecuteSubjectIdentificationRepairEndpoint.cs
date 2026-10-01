using EventFlow.EntityFramework;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Repairs;

namespace HorseRacingPrediction.Api.Endpoints.Repairs;

using static HorseRacingPrediction.Api.Endpoints.Repairs.SubjectIdentificationRepairService;
using static HorseRacingPrediction.Api.Endpoints.Repairs.HorseIdentityRepairService;


internal static class ExecuteSubjectIdentificationRepairEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/admin/repairs/subject-identification/execute",
                    async (ExecuteSubjectIdentificationRepairRequest request,
                        IDbContextProvider<EventStoreDbContext> provider, CollectionPlatformStore collectionStore,
                        CancellationToken token) =>
                    {
                        if (request.Items is null || request.Items.Count == 0)
                            return Results.BadRequest(new[] { "実行対象を指定してください。" });
                        if (request.Items.Select(x => x.NotificationId).Distinct().Count() != request.Items.Count)
                            return Results.BadRequest(new[] { "NotificationIdが重複しています。" });

                        var active = await collectionStore.GetActionableFailureNotificationsAsync(
                            JstTime.Now(), int.MaxValue, token).ConfigureAwait(false);
                        var byId = active.Where(IsSubjectIdentificationFailure)
                            .ToDictionary(x => x.NotificationId);
                        using var db = provider.CreateContext();
                        var requestedIds = request.Items.Select(x => x.NotificationId).ToArray();
                        var issueById = await db.SubjectIdentificationRepairIssues
                            .Where(x => requestedIds.Contains(x.IssueId) && x.Status == "Open")
                            .ToDictionaryAsync(x => x.IssueId, token).ConfigureAwait(false);
                        var missing = request.Items.Where(x => !byId.ContainsKey(x.NotificationId)
                            && !issueById.ContainsKey(x.NotificationId)).ToArray();
                        if (missing.Length > 0)
                            return Results.Conflict(new[] { "対象の失敗状態が変わりました。再読込してください。" });

                        if (issueById.Count > 0)
                        {
                            if (issueById.Count != request.Items.Count)
                                return Results.Conflict(new[] { "事前判定候補と収集失敗は分けて実行してください。" });
                            var issuePlans = new List<(SubjectIdentificationRepairIssue Issue,
                                (string Id, string Name, Uri? Url) Target)>();
                            foreach (var issue in issueById.Values)
                            {
                                var target = await ResolvePreDispatchRepairTargetAsync(db, issue, token).ConfigureAwait(false);
                                if (target is null)
                                    return Results.Conflict(new[] { $"{issue.SubjectType}/{issue.SubjectId}: 補正先を一意に確認できません。" });
                                issuePlans.Add((issue, target.Value));
                            }
                            var issueReceipts = new List<CollectionRequestReceipt>();
                            foreach (var plan in issuePlans)
                            {
                                var issue = plan.Issue;
                                var target = plan.Target;
                                var recoveryKey = $"subject-repair:{issue.IssueId:N}:{issue.Occurrence}";
                                var recoveryFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                                    string.Join('|', issue.SubjectType, issue.DefinitionId, CollectionDefinitionRevisions.Subject(issue.DefinitionId), target.Id,
                                        target.Url?.AbsoluteUri ?? string.Empty)))).ToLowerInvariant();
                                CollectionRequestReceipt receipt;
                                try
                                {
                                    receipt = await collectionStore.RequestAsync(
                                        new(Enum.Parse<CollectionResourceType>(issue.SubjectType), "JRA", target.Id),
                                        new(issue.DefinitionId), CollectionDefinitionRevisions.Subject(issue.DefinitionId), CollectionReason.Recovery, JstTime.Now(),
                                        CollectionLane.Normal, (int)CollectionPriority.High, target.Url, recoveryKey,
                                        attributes: new Dictionary<string, string>
                                        {
                                            ["name"] = target.Name,
                                            ["requestedByRaceId"] = issue.RequestedByRaceId!,
                                        }, cancellationToken: token, payloadFingerprint: recoveryFingerprint)
                                        .ConfigureAwait(false);
                                }
                                catch (CollectionRequestIdempotencyMismatchException)
                                {
                                    return Results.Conflict(new[]
                                    {
                                        $"{issue.SubjectType}/{issue.SubjectId}: 修復要求後に補正先が変化しました。再度候補を生成してください。",
                                    });
                                }
                                issue.Status = "Resolved"; issue.ResolvedAt = JstTime.Now();
                                issue.TargetSubjectId = target.Id; issue.RecoveryTaskId = receipt.TaskId;
                                issueReceipts.Add(receipt);
                            }
                            await db.SaveChangesAsync(token).ConfigureAwait(false);
                            return Results.Accepted(value: new ExecuteSubjectIdentificationRepairResponse(new SubjectIdentificationExecutionDto(
                                request.Items.Count, issueReceipts.Count(x => x.CreatedTask),
                                issueReceipts.Count(x => !x.CreatedTask), issueReceipts.Where(x => x.TaskId.HasValue).Select(x => x.TaskId!.Value).ToArray())));
                        }

                        var horsePreview = await BuildHorseIdentityRepairPreviewAsync(db, collectionStore, token)
                            .ConfigureAwait(false);
                        var plans = new List<(PendingCollectionFailureNotification Failure, Uri? Url, int Revision,
                            HorseIdentityRepairCandidateDto? Merge, string? RedirectTarget)>();
                        foreach (var item in request.Items)
                        {
                            var failure = byId[item.NotificationId];
                            var hasExplicitCorrection = IsValidCorrectionUrl(item.CorrectionUrl);
                            var detail = await collectionStore.GetResourceDetailPagedAsync(
                                failure.Resource, failure.Definition, cancellationToken: token).ConfigureAwait(false);
                            if (HasMissingNameFailure(detail, failure.TaskId))
                                return Results.Conflict(new[]
                                {
                                    $"{failure.Resource.Type}/{failure.Resource.Id}: 主体名がないため再収集できません。",
                                });
                            var storedUrl = detail?.Attempts
                                .Where(x => x.TaskId == failure.TaskId && x.ErrorCode == SubjectNotIdentifiedErrorCode)
                                .OrderByDescending(x => x.StartedAt)
                                .SelectMany(x => new[] { x.FinalUrl, x.RequestedUrl })
                                .FirstOrDefault(IsValidCorrectionUrl);
                            var rawUrl = string.IsNullOrWhiteSpace(item.CorrectionUrl) ? storedUrl : item.CorrectionUrl;
                            if (!TryNormalizeCorrectionUrl(rawUrl, out var correctionUrl))
                                return Results.BadRequest(new[]
                                {
                                    $"{failure.Resource.Type}/{failure.Resource.Id}: JRAの主体プロフィールURLとして解釈できません。",
                                });
                            var state = await collectionStore.GetStateAsync(failure.Resource, failure.Definition, token)
                                .ConfigureAwait(false);
                            if (state is null)
                                return Results.Conflict(new[] { $"{failure.Resource}: 収集状態が見つかりません。" });
                            HorseIdentityRepairCandidateDto? merge = null;
                            string? redirectTarget = null;
                            if (failure.Resource.Type == CollectionResourceType.Horse)
                            {
                                var matching = horsePreview.Candidates
                                    .Where(x => x.SourceHorseId == failure.Resource.Id).ToArray();
                                if (matching.Length > 1)
                                    return Results.Conflict(new[]
                                    {
                                        $"{failure.Resource.Id}: 名寄せ先が複数あります。RaceEntryを再確認してください。",
                                    });
                                merge = matching.SingleOrDefault();
                                if (merge is { SafeToApply: false })
                                    return Results.Conflict(new[] { $"{merge.CandidateId}: {merge.BlockingReason}" });
                                if (merge is null)
                                    redirectTarget = await db.HorseIdentityRepairRedirects.AsNoTracking()
                                        .Where(x => x.SourceHorseId == failure.Resource.Id
                                            && x.RepairId == HorseIdentityRepairId)
                                        .Select(x => x.TargetHorseId).SingleOrDefaultAsync(token).ConfigureAwait(false);
                            }
                            if (merge is null && redirectTarget is null && !hasExplicitCorrection
                                && (!IsValidCorrectionUrl(storedUrl)
                                    || IsUnsafeStoredRepairEvidence(failure.Resource.Type, failure.ErrorMessage)))
                                return Results.Conflict(new[]
                                {
                                    $"{failure.Resource.Type}/{failure.Resource.Id}: " +
                                    "同一性を証明できる補正先がありません。対応不要として閉じるか、確認済みURLを指定してください。",
                                });
                            plans.Add((failure, correctionUrl, state.RequiredRevision, merge, redirectTarget));
                        }

                        var receipts = new List<CollectionRequestReceipt>(plans.Count);
                        var merged = 0;
                        var disabled = 0;
                        var running = 0;
                        foreach (var plan in plans)
                        {
                            var recoveryResource = plan.Failure.Resource;
                            if (plan.Merge is not null)
                            {
                                await ApplyHorseMergeAsync(db, plan.Merge, token).ConfigureAwait(false);
                                recoveryResource = new(CollectionResourceType.Horse, plan.Failure.Resource.Provider,
                                    plan.Merge.TargetHorseId);
                                merged++;
                            }
                            else if (plan.RedirectTarget is not null)
                            {
                                recoveryResource = new(CollectionResourceType.Horse, plan.Failure.Resource.Provider,
                                    plan.RedirectTarget);
                            }
                            receipts.Add(await collectionStore.RequestAsync(recoveryResource, plan.Failure.Definition,
                                plan.Revision, CollectionReason.Recovery, JstTime.Now(),
                                CollectionLane.Normal, (int)CollectionPriority.High, plan.Url,
                                cancellationToken: token).ConfigureAwait(false));
                            if (recoveryResource != plan.Failure.Resource)
                            {
                                var suppression = await collectionStore.SuppressResourceAsync(plan.Failure.Resource,
                                    "JRA競走馬識別子の補正により統合先で再収集します。",
                                    HorseIdentityRepairId, JstTime.Now(), token).ConfigureAwait(false);
                                disabled += suppression.CancelledTasks;
                                running += suppression.RunningCancellationRequests;
                            }
                        }

                        return Results.Accepted(value: new ExecuteSubjectIdentificationRepairResponse(new SubjectIdentificationExecutionDto(
                            request.Items.Count, receipts.Count(x => x.CreatedTask), receipts.Count(x => !x.CreatedTask),
                            receipts.Where(x => x.TaskId.HasValue).Select(x => x.TaskId!.Value).Distinct().ToArray(), merged, disabled, running)));
                    })
                    .Produces<ExecuteSubjectIdentificationRepairResponse>(StatusCodes.Status202Accepted)
                    .Produces(StatusCodes.Status400BadRequest)
                    .Produces(StatusCodes.Status409Conflict);
    }
}
