using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.RegularExpressions;

using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Repairs;

namespace HorseRacingPrediction.Api.Endpoints.Repairs;

using static HorseRacingPrediction.Api.Endpoints.Repairs.HorseIdentityRepairService;

internal static partial class SubjectIdentificationRepairService
{
    internal const string SubjectNotIdentifiedErrorCode = "SubjectNotIdentified";


    internal static async Task<IReadOnlyList<SubjectIdentificationRepairCandidateDto>>
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
        var result = new List<SubjectIdentificationRepairCandidateDto>(failures.Length);
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
            var merges = failure.Resource.Type == CollectionResourceType.Horse
                ? horsePreview.Candidates.Where(x => x.SourceHorseId == failure.Resource.Id).ToArray()
                : [];
            var merge = merges.Length == 1 ? merges[0] : null;
            var redirectedTarget = failure.Resource.Type == CollectionResourceType.Horse
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
            result.Add(new(issue.IssueId, Guid.Empty, Enum.Parse<CollectionResourceType>(issue.SubjectType),
                issue.SubjectId, issue.DefinitionId, issue.ReasonMessage, issue.CreatedAt,
                target is null ? "Blocked" : "MergeReady", target is not null,
                target is null ? "補正先を一意に確認できません。" : null, issue.SourceUrl,
                MergeTargetId: target?.Id));
        }
        return result;
    }

    internal static async Task<(string Id, string Name, Uri? Url)?> ResolvePreDispatchRepairTargetAsync(
        EventStoreDbContext db, SubjectIdentificationRepairIssue issue, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(issue.RequestedByRaceId)) return null;
        var race = await db.RacePredictionContexts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.RaceId == issue.RequestedByRaceId, token).ConfigureAwait(false);
        if (race is null || !Enum.TryParse<CollectionResourceType>(issue.SubjectType, out var type)) return null;
        var ids = type switch
        {
            CollectionResourceType.Horse => race.Entries.Select(x => x.HorseId),
            CollectionResourceType.Jockey => race.Entries.Select(x => x.JockeyId).Where(x => x is not null).Cast<string>(),
            CollectionResourceType.Trainer => race.Entries.Select(x => x.TrainerId).Where(x => x is not null).Cast<string>(),
            _ => [],
        };
        var candidates = new List<(string Id, string Name)>();
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            var name = type switch
            {
                CollectionResourceType.Horse => await db.Horses.AsNoTracking().Where(x => x.HorseId == id)
                    .Select(x => x.RegisteredName).SingleOrDefaultAsync(token),
                CollectionResourceType.Jockey => await db.Jockeys.AsNoTracking().Where(x => x.JockeyId == id)
                    .Select(x => x.DisplayName).SingleOrDefaultAsync(token),
                CollectionResourceType.Trainer => await db.Trainers.AsNoTracking().Where(x => x.TrainerId == id)
                    .Select(x => x.DisplayName).SingleOrDefaultAsync(token),
                _ => null,
            };
            if (name is null) continue;
            var normalized = JraSubjectNameNormalizer.NormalizeIdentityName(type.ToString(), name);
            var expectedName = JraSubjectNameNormalizer.NormalizeIdentityName(type.ToString(), issue.SubjectName);
            if (!string.Equals(normalized, expectedName, StringComparison.Ordinal)) continue;
            var canonical = JraSubjectNameNormalizer.CanonicalizeDisplayName(type.ToString(), name);
            string expectedId;
            if (type == CollectionResourceType.Horse)
            {
                try { expectedId = await CollectionIdentityResolver.HorseAsync(db, name, issue.SourceIdentity, null, token); }
                catch (InvalidOperationException) { return null; }
            }
            else expectedId = DeterministicIdGenerator.BuildEntityId(
                    type == CollectionResourceType.Jockey ? "jockey" : "trainer",
                    DeterministicIdGenerator.NormalizeKey(canonical));
            if (id == expectedId) candidates.Add((id, name));
        }
        if (candidates.DistinctBy(x => x.Id).Count() != 1) return null;
        var target = candidates[0];
        return (target.Id, target.Name,
            TryNormalizeCorrectionUrl(issue.SourceUrl, out var url) ? url : null);
    }

    internal static bool IsSubjectIdentificationFailure(PendingCollectionFailureNotification failure) =>
        (string.Equals(failure.ErrorCode, SubjectNotIdentifiedErrorCode, StringComparison.Ordinal)
            || string.Equals(failure.ErrorCode, "SubjectResourceMissing", StringComparison.Ordinal)
            || string.Equals(failure.ErrorCode, "SubjectProjectionNotReady", StringComparison.Ordinal))
        && failure.Resource.Type is CollectionResourceType.Horse or CollectionResourceType.Jockey
            or CollectionResourceType.Trainer or CollectionResourceType.Owner;

    internal static bool IsObsoleteSubjectReference(string? message) =>
        message?.Contains(" 産駒", StringComparison.Ordinal) == true
        || message?.Contains("公開検索に一致候補がありません", StringComparison.Ordinal) == true;

    internal static bool IsUnsafeStoredRepairEvidence(CollectionResourceType resourceType, string? message) =>
        IsObsoleteSubjectReference(message)
        || (message?.Contains("取得プロフィールの名前が一致しません", StringComparison.Ordinal) == true
            && (resourceType != CollectionResourceType.Horse
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
    internal static partial Regex StructuredHorseNameMismatch();

    [GeneratedRegex(@"^(?:マルガイ|マルチ|マル外|マル地)\s*")]
    internal static partial Regex HorseRegistrationMarkPrefix();

    internal static bool IsValidCorrectionUrl(string? value) => TryCreateCorrectionUrl(value, out _);

    internal static bool HasMissingNameFailure(CollectionResourceDetail? detail, Guid taskId) =>
        detail?.Attempts.Any(x => x.TaskId == taskId
            && x.ErrorCode == SubjectNotIdentifiedErrorCode
            && string.Equals(x.PageIdentification, "SubjectIdentification:MissingName",
                StringComparison.Ordinal)) == true;

    internal static async Task ApplyHorseMergeAsync(EventStoreDbContext db,
        HorseIdentityRepairCandidateDto candidate, CancellationToken token)
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

    internal static bool TryCreateCorrectionUrl(string? value, out Uri? url)
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

    internal static bool TryNormalizeCorrectionUrl(string? value, out Uri? url)
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
