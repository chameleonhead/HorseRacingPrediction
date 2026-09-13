using EventFlow.EntityFramework;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Time;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.Api;

public static partial class EndpointExtensions
{
    private const string SubjectNotIdentifiedErrorCode = "SubjectNotIdentified";

    private static void MapSubjectIdentificationRepairEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/admin/repairs/subject-identification",
            async (IDbContextProvider<EventStoreDbContext> provider, CollectionPlatformStore collectionStore,
                CancellationToken token) =>
            {
                using var db = provider.CreateContext();
                var candidates = await BuildSubjectIdentificationRepairPreviewAsync(db, collectionStore, token)
                    .ConfigureAwait(false);
                return Results.Ok(new SubjectIdentificationRepairPreviewResponse(candidates));
            });

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
                var missing = request.Items.Where(x => !byId.ContainsKey(x.NotificationId)).ToArray();
                if (missing.Length > 0)
                    return Results.Conflict(new[] { "対象の失敗状態が変わりました。再読込してください。" });

                using var db = provider.CreateContext();
                var horsePreview = await BuildHorseIdentityRepairPreviewAsync(db, collectionStore, token)
                    .ConfigureAwait(false);
                var plans = new List<(PendingCollectionFailureNotification Failure, Uri Url, int Revision,
                    HorseIdentityRepairCandidateResponse? Merge, string? RedirectTarget)>();
                foreach (var item in request.Items)
                {
                    var failure = byId[item.NotificationId];
                    var detail = await collectionStore.GetResourceDetailPagedAsync(
                        failure.Resource, failure.Definition, cancellationToken: token).ConfigureAwait(false);
                    var storedUrl = detail?.Attempts
                        .Where(x => x.TaskId == failure.TaskId && x.ErrorCode == SubjectNotIdentifiedErrorCode)
                        .OrderByDescending(x => x.StartedAt)
                        .SelectMany(x => new[] { x.FinalUrl, x.RequestedUrl })
                        .FirstOrDefault(IsValidCorrectionUrl);
                    var rawUrl = string.IsNullOrWhiteSpace(item.CorrectionUrl) ? storedUrl : item.CorrectionUrl;
                    if (!TryCreateCorrectionUrl(rawUrl, out var correctionUrl))
                        return Results.BadRequest(new[]
                        {
                            $"{failure.Resource.Type}/{failure.Resource.Id}: パラメーターを含む有効なJRA URLを指定してください。",
                        });
                    var state = await collectionStore.GetStateAsync(failure.Resource, failure.Definition, token)
                        .ConfigureAwait(false);
                    if (state is null)
                        return Results.Conflict(new[] { $"{failure.Resource}: 収集状態が見つかりません。" });
                    HorseIdentityRepairCandidateResponse? merge = null;
                    string? redirectTarget = null;
                    if (failure.Resource.Type == ResourceType.Horse)
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
                    plans.Add((failure, correctionUrl!, state.RequiredRevision, merge, redirectTarget));
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
                        recoveryResource = new(ResourceType.Horse, plan.Failure.Resource.Provider,
                            plan.Merge.TargetHorseId);
                        merged++;
                    }
                    else if (plan.RedirectTarget is not null)
                    {
                        recoveryResource = new(ResourceType.Horse, plan.Failure.Resource.Provider,
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

                return Results.Accepted(value: new ExecuteSubjectIdentificationRepairResponse(
                    request.Items.Count, receipts.Count(x => x.CreatedTask), receipts.Count(x => !x.CreatedTask),
                    receipts.Select(x => x.TaskId).Distinct().ToArray(), merged, disabled, running));
            });
    }

    private static async Task<IReadOnlyList<SubjectIdentificationRepairCandidateResponse>>
        BuildSubjectIdentificationRepairPreviewAsync(EventStoreDbContext db,
            CollectionPlatformStore collectionStore, CancellationToken token)
    {
        var failures = (await collectionStore.GetActionableFailureNotificationsAsync(
                JstTime.Now(), int.MaxValue, token).ConfigureAwait(false))
            .Where(IsSubjectIdentificationFailure).OrderByDescending(x => x.FailedAt).ToArray();
        var horsePreview = await BuildHorseIdentityRepairPreviewAsync(db, collectionStore, token)
            .ConfigureAwait(false);
        var horseRedirects = await db.HorseIdentityRepairRedirects.AsNoTracking()
            .Where(x => x.RepairId == HorseIdentityRepairId)
            .ToDictionaryAsync(x => x.SourceHorseId, x => x.TargetHorseId, token).ConfigureAwait(false);
        var result = new List<SubjectIdentificationRepairCandidateResponse>(failures.Length);
        foreach (var failure in failures)
        {
            var detail = await collectionStore.GetResourceDetailPagedAsync(
                failure.Resource, failure.Definition, cancellationToken: token).ConfigureAwait(false);
            var suggestedUrl = detail?.Attempts
                .Where(x => x.TaskId == failure.TaskId && x.ErrorCode == SubjectNotIdentifiedErrorCode)
                .OrderByDescending(x => x.StartedAt)
                .SelectMany(x => new[] { x.FinalUrl, x.RequestedUrl })
                .FirstOrDefault(IsValidCorrectionUrl);
            var merges = failure.Resource.Type == ResourceType.Horse
                ? horsePreview.Candidates.Where(x => x.SourceHorseId == failure.Resource.Id).ToArray()
                : [];
            var merge = merges.Length == 1 ? merges[0] : null;
            var redirectedTarget = failure.Resource.Type == ResourceType.Horse
                ? horseRedirects.GetValueOrDefault(failure.Resource.Id)
                : null;
            var blocked = merges.Length > 1 ? "同じ統合元に複数の統合先候補があります。"
                : merge is { SafeToApply: false } ? merge.BlockingReason
                : suggestedUrl is null ? "有効なJRA URLを確認できません。補正URLを入力してください。"
                : null;
            var ready = blocked is null;
            result.Add(new(failure.NotificationId, failure.TaskId, failure.Resource.Type,
                failure.Resource.Id, failure.Definition.Value, failure.ErrorMessage, failure.FailedAt,
                ready ? merge is null && redirectedTarget is null ? "RetryReady" : "MergeReady" : "Blocked",
                ready, blocked, suggestedUrl, merge?.CandidateId, merge?.TargetHorseId ?? redirectedTarget));
        }
        return result;
    }

    private static bool IsSubjectIdentificationFailure(PendingCollectionFailureNotification failure) =>
        string.Equals(failure.ErrorCode, SubjectNotIdentifiedErrorCode, StringComparison.Ordinal)
        && failure.Resource.Type is ResourceType.Horse or ResourceType.Jockey
            or ResourceType.Trainer or ResourceType.Owner;

    internal static bool IsValidCorrectionUrl(string? value) => TryCreateCorrectionUrl(value, out _);

    private static async Task ApplyHorseMergeAsync(EventStoreDbContext db,
        HorseIdentityRepairCandidateResponse candidate, CancellationToken token)
    {
        var row = await db.HorseIdentityRepairCandidates.SingleAsync(
            x => x.CandidateId == candidate.CandidateId, token).ConfigureAwait(false);
        var redirect = await db.HorseIdentityRepairRedirects.SingleOrDefaultAsync(
            x => x.SourceHorseId == candidate.SourceHorseId, token).ConfigureAwait(false);
        if (redirect is null)
            db.HorseIdentityRepairRedirects.Add(new HorseIdentityRepairRedirectReadModel
            {
                SourceHorseId = candidate.SourceHorseId,
                TargetHorseId = candidate.TargetHorseId,
                RepairId = HorseIdentityRepairId,
                JraIdentity = candidate.JraIdentity,
                CreatedAt = JstTime.Now(),
            });
        else if (redirect.TargetHorseId != candidate.TargetHorseId || redirect.RepairId != HorseIdentityRepairId)
            throw new InvalidOperationException($"{candidate.SourceHorseId}には異なるredirect先があります。");
        row.AppliedAt ??= JstTime.Now();
        await db.SaveChangesAsync(token).ConfigureAwait(false);
    }

    private static bool TryCreateCorrectionUrl(string? value, out Uri? url)
    {
        url = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            || parsed.Scheme is not ("http" or "https")
            || !string.Equals(parsed.Host, "www.jra.go.jp", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(parsed.Query.TrimStart('?'))
            || !parsed.Query.Contains('=', StringComparison.Ordinal)
            || !parsed.AbsolutePath.StartsWith("/JRADB/access", StringComparison.OrdinalIgnoreCase))
            return false;
        url = parsed;
        return true;
    }
}
