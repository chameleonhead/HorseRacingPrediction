using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Time;

namespace HorseRacingPrediction.Api;

internal sealed record SubjectIdentificationAutoRecoveryResult(
    int Examined, int Recovered, int Reused, int Suppressed, int Skipped, int Failed);

internal static class SubjectIdentificationAutoRecovery
{
    internal const string RepairId = "20260918-subject-identification-auto-recovery";
    private sealed record Candidate(PendingCollectionFailureNotification Failure,
        CollectionTaskSummary Task, int CurrentRevision, bool Suppress);

    internal static async Task<int> GetEligibleCandidateCountAsync(
        CollectionPlatformStore store, CancellationToken cancellationToken = default)
    {
        var (candidates, _) = await BuildCandidatesAsync(store, cancellationToken).ConfigureAwait(false);
        return candidates.Count;
    }

    internal static async Task<SubjectIdentificationAutoRecoveryResult> RunOnceAsync(
        CollectionPlatformStore store, ILogger? logger = null, CancellationToken cancellationToken = default,
        int maxCandidates = int.MaxValue)
    {
        var (eligible, skipped) = await BuildCandidatesAsync(store, cancellationToken).ConfigureAwait(false);
        var candidates = eligible.Take(Math.Max(0, maxCandidates)).ToArray();
        var recovered = 0;
        var reused = 0;
        var suppressed = 0;
        var failed = 0;
        foreach (var candidate in candidates)
        {
            var failure = candidate.Failure;
            try
            {
                if (candidate.Suppress)
                {
                    await store.SuppressResourceAsync(failure.Resource,
                        "血統欄の説明文字列から誤生成された収集対象です。", RepairId,
                        JstTime.Now(), cancellationToken).ConfigureAwait(false);
                    suppressed++;
                    continue;
                }

                var receipt = await store.RequestAsync(failure.Resource, failure.Definition,
                    candidate.CurrentRevision, CollectionReason.Recovery, JstTime.Now(),
                    candidate.Task.Lane, candidate.Task.Priority,
                    batchId: $"subject-auto-recovery:{failure.NotificationId:N}:r{candidate.CurrentRevision}",
                    attributes: candidate.Task.Metadata, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (receipt.CreatedTask) recovered++;
                else reused++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failed++;
                logger?.LogError(exception,
                    "Subject identification recovery failed for notification {NotificationId} and resource {Resource}.",
                    failure.NotificationId, failure.Resource);
            }
        }
        return new(candidates.Length, recovered, reused, suppressed, skipped, failed);
    }

    private static async Task<(IReadOnlyList<Candidate> Candidates, int Skipped)> BuildCandidatesAsync(
        CollectionPlatformStore store, CancellationToken cancellationToken)
    {
        var failures = (await store.GetActionableFailureNotificationsAsync(
                JstTime.Now(), int.MaxValue, cancellationToken).ConfigureAwait(false))
            .Where(x => (string.Equals(x.ErrorCode, "SubjectNotIdentified", StringComparison.Ordinal)
                    || string.Equals(x.ErrorCode, "StructuralPageFailure", StringComparison.Ordinal)
                    || x.ErrorMessage?.Contains("情報の見出しを確認できません", StringComparison.Ordinal) == true)
                && x.Resource.Type is ResourceType.Horse or ResourceType.Jockey or ResourceType.Trainer)
            .ToArray();
        var candidates = new List<Candidate>();
        var skipped = 0;
        foreach (var failure in failures)
        {
            var detail = await store.GetResourceDetailPagedAsync(failure.Resource, failure.Definition,
                historyPageSize: 100, cancellationToken: cancellationToken).ConfigureAwait(false);
            var task = detail?.Tasks.FirstOrDefault(x => x.TaskId == failure.TaskId) ?? detail?.LatestTask;
            var attempt = detail?.Attempts.Where(x => x.TaskId == failure.TaskId)
                .OrderByDescending(x => x.StartedAt).FirstOrDefault();
            var name = task?.Metadata?.GetValueOrDefault("name")?.Trim();
            if (task is null || string.IsNullOrWhiteSpace(name)
                || string.Equals(attempt?.PageIdentification, "SubjectIdentification:MissingName",
                    StringComparison.Ordinal))
            {
                skipped++;
                continue;
            }

            var currentRevision = await store.GetCurrentRevisionAsync(failure.Definition, cancellationToken)
                .ConfigureAwait(false);
            if (currentRevision <= task.RequestedRevision)
            {
                skipped++;
                continue;
            }

            var suppress = failure.Resource.Type == ResourceType.Horse
                && name.EndsWith("産駒", StringComparison.Ordinal);
            var structuralFailure = string.Equals(failure.ErrorCode, "StructuralPageFailure", StringComparison.Ordinal)
                || failure.ErrorMessage?.Contains("情報の見出しを確認できません", StringComparison.Ordinal) == true;
            var profileMismatch = string.Equals(attempt?.PageIdentification,
                "SubjectIdentification:ProfileNameMismatch", StringComparison.Ordinal);
            var directorySubject = failure.Resource.Type is ResourceType.Jockey or ResourceType.Trainer;
            if (!suppress && !structuralFailure && !profileMismatch && !directorySubject)
            {
                skipped++;
                continue;
            }

            candidates.Add(new(failure, task, currentRevision, suppress));
        }
        return (candidates, skipped);
    }
}
