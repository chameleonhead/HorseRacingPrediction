using System.Security.Cryptography;
using System.Text;
using EventFlow.EntityFramework;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Contracts.Time;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

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
                            string.Join('|', issue.SubjectType, issue.DefinitionId, 3, target.Id,
                                target.Url?.AbsoluteUri ?? string.Empty)))).ToLowerInvariant();
                        CollectionRequestReceipt receipt;
                        try
                        {
                            receipt = await collectionStore.RequestAsync(
                                new(Enum.Parse<ResourceType>(issue.SubjectType), "JRA", target.Id),
                                new(issue.DefinitionId), 3, CollectionReason.Recovery, JstTime.Now(),
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
                    return Results.Accepted(value: new ExecuteSubjectIdentificationRepairResponse(
                        request.Items.Count, issueReceipts.Count(x => x.CreatedTask),
                        issueReceipts.Count(x => !x.CreatedTask), issueReceipts.Where(x => x.TaskId.HasValue).Select(x => x.TaskId!.Value).ToArray()));
                }

                var horsePreview = await BuildHorseIdentityRepairPreviewAsync(db, collectionStore, token)
                    .ConfigureAwait(false);
                var plans = new List<(PendingCollectionFailureNotification Failure, Uri? Url, int Revision,
                    HorseIdentityRepairCandidateResponse? Merge, string? RedirectTarget)>();
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
                    receipts.Where(x => x.TaskId.HasValue).Select(x => x.TaskId!.Value).Distinct().ToArray(), merged, disabled, running));
            });

        group.MapPost("/admin/repairs/subject-identification/dismiss",
            async (DismissSubjectIdentificationFailuresRequest request, CollectionPlatformStore collectionStore,
                CancellationToken token) =>
            {
                if (request.NotificationIds is null || request.NotificationIds.Count == 0)
                    return Results.BadRequest(new[] { "対応不要にする対象を指定してください。" });
                if (request.NotificationIds.Distinct().Count() != request.NotificationIds.Count)
                    return Results.BadRequest(new[] { "NotificationIdが重複しています。" });

                var selected = await collectionStore.GetFailureNotificationsAsync(request.NotificationIds, token)
                    .ConfigureAwait(false);
                if (selected.Count != request.NotificationIds.Count || selected.Any(x => !IsSubjectIdentificationFailure(x)))
                    return Results.BadRequest(new[] { "主体識別情報の補正候補ではない対象が含まれています。" });

                CollectionFailureDismissalResult result;
                try
                {
                    result = await collectionStore.DismissFailureNotificationsAsync(
                        request.NotificationIds, JstTime.Now(), token).ConfigureAwait(false);
                }
                catch (KeyNotFoundException)
                {
                    return Results.Conflict(new[] { "対象の失敗状態が変わりました。再読込してください。" });
                }
                if (result.HasRecoveryConflict)
                    return Results.Conflict(new[] { "再収集が開始された対象が含まれています。再読込してください。" });

                return Results.Ok(new DismissSubjectIdentificationFailuresResponse(
                    result.SelectedCount, result.DismissedCount, result.AlreadyClosedCount));
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
            var missingName = HasMissingNameFailure(detail, failure.TaskId);
            var merges = failure.Resource.Type == ResourceType.Horse
                ? horsePreview.Candidates.Where(x => x.SourceHorseId == failure.Resource.Id).ToArray()
                : [];
            var merge = merges.Length == 1 ? merges[0] : null;
            var redirectedTarget = failure.Resource.Type == ResourceType.Horse
                ? horseRedirects.GetValueOrDefault(failure.Resource.Id)
                : null;
            var blocked = missingName ? "主体名がないため再収集できません。"
                : merges.Length > 1 ? "同じ統合元に複数の統合先候補があります。"
                : merge is { SafeToApply: false } ? merge.BlockingReason
                : merge is null && redirectedTarget is null
                    && (!IsValidCorrectionUrl(suggestedUrl)
                        || IsUnsafeStoredRepairEvidence(failure.Resource.Type, failure.ErrorMessage))
                    ? "同一性を証明できる補正先がありません。同じ条件では再失敗するため、対応不要として閉じるか確認済みURLを指定してください。"
                : null;
            var ready = blocked is null;
            result.Add(new(failure.NotificationId, failure.TaskId, failure.Resource.Type,
                failure.Resource.Id, failure.Definition.Value, failure.ErrorMessage, failure.FailedAt,
                ready ? merge is null && redirectedTarget is null ? "RetryReady" : "MergeReady"
                    : IsObsoleteSubjectReference(failure.ErrorMessage) ? "DismissRecommended" : "Blocked",
                ready, blocked, suggestedUrl, merge?.CandidateId, merge?.TargetHorseId ?? redirectedTarget));
        }
        var issues = await db.SubjectIdentificationRepairIssues.AsNoTracking()
            .Where(x => x.Status == "Open").OrderByDescending(x => x.CreatedAt).ToArrayAsync(token)
            .ConfigureAwait(false);
        foreach (var issue in issues)
        {
            var target = await ResolvePreDispatchRepairTargetAsync(db, issue, token).ConfigureAwait(false);
            result.Add(new(issue.IssueId, Guid.Empty, Enum.Parse<ResourceType>(issue.SubjectType),
                issue.SubjectId, issue.DefinitionId, issue.ReasonMessage, issue.CreatedAt,
                target is null ? "Blocked" : "MergeReady", target is not null,
                target is null ? "補正先を一意に確認できません。" : null, issue.SourceUrl,
                MergeTargetId: target?.Id));
        }
        return result;
    }

    private static async Task<(string Id, string Name, Uri? Url)?> ResolvePreDispatchRepairTargetAsync(
        EventStoreDbContext db, SubjectIdentificationRepairIssue issue, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(issue.RequestedByRaceId)) return null;
        var race = await db.RacePredictionContexts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.RaceId == issue.RequestedByRaceId, token).ConfigureAwait(false);
        if (race is null || !Enum.TryParse<ResourceType>(issue.SubjectType, out var type)) return null;
        var ids = type switch
        {
            ResourceType.Horse => race.Entries.Select(x => x.HorseId),
            ResourceType.Jockey => race.Entries.Select(x => x.JockeyId).Where(x => x is not null).Cast<string>(),
            ResourceType.Trainer => race.Entries.Select(x => x.TrainerId).Where(x => x is not null).Cast<string>(),
            _ => [],
        };
        var candidates = new List<(string Id, string Name)>();
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            var name = type switch
            {
                ResourceType.Horse => await db.Horses.AsNoTracking().Where(x => x.HorseId == id)
                    .Select(x => x.RegisteredName).SingleOrDefaultAsync(token),
                ResourceType.Jockey => await db.Jockeys.AsNoTracking().Where(x => x.JockeyId == id)
                    .Select(x => x.DisplayName).SingleOrDefaultAsync(token),
                ResourceType.Trainer => await db.Trainers.AsNoTracking().Where(x => x.TrainerId == id)
                    .Select(x => x.DisplayName).SingleOrDefaultAsync(token),
                _ => null,
            };
            if (name is null) continue;
            var normalized = HorseRacingPrediction.Contracts.JraSubjectNameNormalizer.NormalizeIdentityName(type.ToString(), name);
            var expectedName = HorseRacingPrediction.Contracts.JraSubjectNameNormalizer.NormalizeIdentityName(type.ToString(), issue.SubjectName);
            if (!string.Equals(normalized, expectedName, StringComparison.Ordinal)) continue;
            var canonical = HorseRacingPrediction.Contracts.JraSubjectNameNormalizer.CanonicalizeDisplayName(type.ToString(), name);
            var expectedId = type == ResourceType.Horse
                ? HorseRacingPrediction.ApiClient.DeterministicIdGenerator.BuildHorseId(canonical,
                    HorseRacingPrediction.ApiClient.JraSourceIdentity.TryNormalizeHorse(issue.SourceIdentity, out _)
                        ? issue.SourceIdentity : null)
                : HorseRacingPrediction.ApiClient.DeterministicIdGenerator.BuildEntityId(
                    type == ResourceType.Jockey ? "jockey" : "trainer",
                    HorseRacingPrediction.ApiClient.DeterministicIdGenerator.NormalizeKey(canonical));
            if (id == expectedId) candidates.Add((id, name));
        }
        if (candidates.DistinctBy(x => x.Id).Count() != 1) return null;
        var target = candidates[0];
        return (target.Id, target.Name,
            TryNormalizeCorrectionUrl(issue.SourceUrl, out var url) ? url : null);
    }

    private static bool IsSubjectIdentificationFailure(PendingCollectionFailureNotification failure) =>
        (string.Equals(failure.ErrorCode, SubjectNotIdentifiedErrorCode, StringComparison.Ordinal)
            || string.Equals(failure.ErrorCode, "SubjectResourceMissing", StringComparison.Ordinal)
            || string.Equals(failure.ErrorCode, "SubjectProjectionNotReady", StringComparison.Ordinal))
        && failure.Resource.Type is ResourceType.Horse or ResourceType.Jockey
            or ResourceType.Trainer or ResourceType.Owner;

    private static bool IsObsoleteSubjectReference(string? message) =>
        message?.Contains(" 産駒", StringComparison.Ordinal) == true
        || message?.Contains("公開検索に一致候補がありません", StringComparison.Ordinal) == true;

    private static bool IsUnsafeStoredRepairEvidence(ResourceType resourceType, string? message) =>
        IsObsoleteSubjectReference(message)
        || (message?.Contains("取得プロフィールの名前が一致しません", StringComparison.Ordinal) == true
            && (resourceType != ResourceType.Horse
                || !IsCorrectableHorseRegistrationMarkMismatch(message)))
        || message?.Contains("取得したプロフィールの名前が対象と一致しません", StringComparison.Ordinal) == true
        || message?.Contains("公開識別子が一致しません", StringComparison.Ordinal) == true;

    internal static bool IsCorrectableHorseRegistrationMarkMismatch(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        var match = StructuredHorseNameMismatch().Match(message);
        if (!match.Success) return false;
        var expected = match.Groups["expected"].Value.Trim();
        var actual = match.Groups["actual"].Value.Trim();
        if (!HorseRegistrationMarkPrefix().IsMatch(actual.Normalize(NormalizationForm.FormKC))) return false;
        var normalizedExpected = JraSubjectNameNormalizer.NormalizeIdentityName("Horse", expected);
        var normalizedActual = JraSubjectNameNormalizer.NormalizeIdentityName("Horse", actual);
        return normalizedExpected.Length > 0
            && string.Equals(normalizedExpected, normalizedActual, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"期待=Horse::(?<expected>[^;]+);\s*取得名=(?<actual>[^;]+)(?:;|$)")]
    private static partial Regex StructuredHorseNameMismatch();

    [GeneratedRegex(@"^(?:マルガイ|マルチ|マル外|マル地)\s*")]
    private static partial Regex HorseRegistrationMarkPrefix();

    internal static bool IsValidCorrectionUrl(string? value) => TryCreateCorrectionUrl(value, out _);

    private static bool HasMissingNameFailure(CollectionResourceDetail? detail, Guid taskId) =>
        detail?.Attempts.Any(x => x.TaskId == taskId
            && x.ErrorCode == SubjectNotIdentifiedErrorCode
            && string.Equals(x.PageIdentification, "SubjectIdentification:MissingName",
                StringComparison.Ordinal)) == true;

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

    private static bool TryNormalizeCorrectionUrl(string? value, out Uri? url)
    {
        url = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (IsParameterlessJraAccessUrl(value)) return true;
        return TryCreateCorrectionUrl(value, out url);
    }

    internal static bool IsParameterlessJraAccessUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && string.Equals(uri.Host, "www.jra.go.jp", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith("/JRADB/access", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrWhiteSpace(uri.Query.TrimStart('?'));
}
