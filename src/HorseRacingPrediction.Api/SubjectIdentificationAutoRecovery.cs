using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Time;

namespace HorseRacingPrediction.Api;

internal sealed record SubjectIdentificationAutoRecoveryResult(
    int Examined, int Recovered, int Reused, int Suppressed, int Skipped, int Failed);

internal static class SubjectIdentificationAutoRecovery
{
    internal const string RepairId = "20260918-subject-identification-auto-recovery";

    internal static async Task<SubjectIdentificationAutoRecoveryResult> RunOnceAsync(
        CollectionPlatformStore store, ILogger? logger = null, CancellationToken cancellationToken = default,
        int maxCandidates = int.MaxValue)
    {
        var failures = (await store.GetActionableFailureNotificationsAsync(
                JstTime.Now(), int.MaxValue, cancellationToken).ConfigureAwait(false))
            .Where(x => (string.Equals(x.ErrorCode, "SubjectNotIdentified", StringComparison.Ordinal)
                    || string.Equals(x.ErrorCode, "StructuralPageFailure", StringComparison.Ordinal)
                    || x.ErrorMessage?.Contains("情報の見出しを確認できません", StringComparison.Ordinal) == true)
                && x.Resource.Type is ResourceType.Horse or ResourceType.Jockey or ResourceType.Trainer)
            .Take(Math.Max(0, maxCandidates))
            .ToArray();
        var recovered = 0;
        var reused = 0;
        var suppressed = 0;
        var skipped = 0;
        var failed = 0;
        foreach (var failure in failures)
        {
            try
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

                if (failure.Resource.Type == ResourceType.Horse
                    && name.EndsWith("産駒", StringComparison.Ordinal))
                {
                    await store.SuppressResourceAsync(failure.Resource,
                        "血統欄の説明文字列から誤生成された収集対象です。", RepairId,
                        JstTime.Now(), cancellationToken).ConfigureAwait(false);
                    suppressed++;
                    continue;
                }

                var structuralFailure = string.Equals(failure.ErrorCode, "StructuralPageFailure", StringComparison.Ordinal)
                    || failure.ErrorMessage?.Contains("情報の見出しを確認できません", StringComparison.Ordinal) == true;
                var profileMismatch = string.Equals(attempt?.PageIdentification,
                    "SubjectIdentification:ProfileNameMismatch", StringComparison.Ordinal);
                var directorySubject = failure.Resource.Type is ResourceType.Jockey or ResourceType.Trainer;
                if (!structuralFailure && !profileMismatch && !directorySubject)
                {
                    skipped++;
                    continue;
                }

                var receipt = await store.RequestAsync(failure.Resource, failure.Definition, currentRevision,
                    CollectionReason.Recovery, JstTime.Now(), task.Lane, task.Priority,
                    batchId: $"subject-auto-recovery:{failure.NotificationId:N}:r{currentRevision}",
                    attributes: task.Metadata, cancellationToken: cancellationToken).ConfigureAwait(false);
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
        return new(failures.Length, recovered, reused, suppressed, skipped, failed);
    }
}
