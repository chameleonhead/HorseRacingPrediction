using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Common;

namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public sealed partial class CollectionPlatformStore
{
    public async Task<SubjectIdentificationCandidateApplication?> GetSubjectIdentificationSelectionAsync(
        Guid notificationId, CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var selection = await db.SubjectIdentificationSelections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.NotificationId == notificationId, cancellationToken)
            .ConfigureAwait(false);
        return selection is null ? null : new(notificationId, selection.SelectedName,
            new Uri(selection.SelectedUrl), selection.Evidence,
            new(selection.ResourceType, selection.Provider, selection.ResourceId),
            new(selection.DefinitionId), selection.CanonicalTaskId, selection.Selector,
            selection.SelectedAt, selection.CreatedTask, selection.ReusedTask);
    }

    public async Task<SubjectIdentificationCandidateApplication> ReserveSubjectIdentificationCandidateAsync(
        Guid notificationId, SubjectIdentificationCandidate candidate, ResourceKey canonicalResource,
        CollectionDefinitionId canonicalDefinition, Uri normalizedUrl, string? selector,
        DateTimeOffset selectedAt,
        CancellationToken cancellationToken = default)
    {
        if (notificationId == Guid.Empty) throw new ArgumentException("Notification id is required.", nameof(notificationId));
        if (canonicalResource.Type != CollectionResourceType.Horse)
            throw new ArgumentException("Candidate selection can only recover a Horse resource.", nameof(canonicalResource));
        if (normalizedUrl is null) throw new ArgumentNullException(nameof(normalizedUrl));
        if (string.IsNullOrWhiteSpace(candidate.Name)) throw new ArgumentException("Candidate name is required.", nameof(candidate));

        // Do not use the process-local store gate here. The notification primary key is
        // the durable claim, and this path must provide the same first-writer semantics
        // when two API processes share the CollectionPlatform database.
        var contentionRetries = 0;
        while (true)
        {
            await using var db = CreateDbContext();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var existing = await db.SubjectIdentificationSelections.SingleOrDefaultAsync(
                x => x.NotificationId == notificationId, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
                return ValidateExistingSelection(existing, notificationId, candidate, canonicalResource,
                    canonicalDefinition, normalizedUrl);

            var row = await (from notification in db.FailureNotifications
                             join task in db.Tasks on notification.TaskId equals task.TaskId
                             join resource in db.Resources on task.ResourcePk equals resource.ResourcePk
                             where notification.NotificationId == notificationId
                             select new { notification, task, resource }).SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (row is null || row.notification.ResolutionStatus != CollectionFailureResolutionStatus.Open
                || row.resource.Type != CollectionResourceType.Horse
                || !string.Equals(row.notification.ErrorCode, "SubjectNotIdentified", StringComparison.Ordinal))
                throw new InvalidOperationException("The subject identification failure is no longer actionable.");
            if (!string.Equals(row.task.DefinitionId, canonicalDefinition.Value, StringComparison.Ordinal))
                throw new InvalidOperationException("The canonical collection definition does not match the failed task.");

            var attempt = await db.Attempts.AsNoTracking().Where(x => x.TaskId == row.task.TaskId)
                .OrderByDescending(x => x.AttemptNumber).FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (attempt is null || !string.Equals(attempt.PageIdentification,
                    "SubjectIdentification:MultipleCandidates", StringComparison.Ordinal))
                throw new InvalidOperationException("The failed attempt has no selectable subject candidates.");
            var storedCandidates = DeserializeIdentificationCandidates(attempt.IdentificationCandidatesJson);
            if (storedCandidates is null || !storedCandidates.Any(stored => CandidateEquals(stored, candidate)))
                throw new InvalidOperationException("The selected subject candidate is not one of the stored candidates.");

            row.notification.ResolutionStatus = CollectionFailureResolutionStatus.RecoveryInProgress;
            row.notification.SelectedUrl = normalizedUrl.AbsoluteUri;
            row.notification.CanonicalResourceType = canonicalResource.Type;
            row.notification.CanonicalResourceProvider = canonicalResource.Provider;
            row.notification.CanonicalResourceId = canonicalResource.Id;
            row.notification.Selector = selector;
            row.notification.SelectedAt = selectedAt;
            db.SubjectIdentificationSelections.Add(new SubjectIdentificationSelectionEntity
            {
                NotificationId = notificationId,
                TaskId = row.task.TaskId,
                SelectedName = candidate.Name,
                SelectedUrl = normalizedUrl.AbsoluteUri,
                Evidence = candidate.Evidence,
                ResourceType = canonicalResource.Type,
                Provider = canonicalResource.Provider,
                ResourceId = canonicalResource.Id,
                DefinitionId = canonicalDefinition.Value,
                // A pending reservation must remain distinguishable from a finalized task.
                CanonicalTaskId = Guid.Empty,
                Selector = selector,
                SelectedAt = selectedAt,
                CreatedTask = false,
                ReusedTask = false,
            });
            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new(notificationId, candidate.Name, normalizedUrl, candidate.Evidence, canonicalResource,
                    canonicalDefinition, Guid.Empty, selector, selectedAt, false, false);
            }
            catch (DbUpdateException ex) when (IsSelectionReservationRace(ex))
            {
                // Another process may have won the notification-keyed insert, or SQLite
                // may have reported a transient writer/snapshot conflict. The transaction
                // is discarded before observing the durable winner.
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                await using var retryDb = CreateDbContext();
                var winner = await retryDb.SubjectIdentificationSelections.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.NotificationId == notificationId, cancellationToken)
                    .ConfigureAwait(false);
                if (winner is not null)
                    return ValidateExistingSelection(winner, notificationId, candidate, canonicalResource,
                        canonicalDefinition, normalizedUrl);
                if (contentionRetries++ < 2)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(25 * contentionRetries), cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }
                throw;
            }
        }
    }

    private static bool IsSelectionReservationRace(DbUpdateException exception)
    {
        var sqlite = exception.InnerException as SqliteException;
        return sqlite is not null && sqlite.SqliteErrorCode is 5 or 6 or 19;
    }

    public async Task<SubjectIdentificationCandidateApplication> FinalizeSubjectIdentificationCandidateAsync(
        Guid notificationId, Guid canonicalTaskId, bool createdTask, bool reusedTask,
        DateTimeOffset finalizedAt, CancellationToken cancellationToken = default)
    {
        if (notificationId == Guid.Empty) throw new ArgumentException("Notification id is required.", nameof(notificationId));
        if (canonicalTaskId == Guid.Empty) throw new ArgumentException("Canonical task id is required.", nameof(canonicalTaskId));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var selection = await db.SubjectIdentificationSelections.SingleOrDefaultAsync(
                x => x.NotificationId == notificationId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The subject identification selection reservation was not found.");
            if (selection.CanonicalTaskId != Guid.Empty && selection.CanonicalTaskId != canonicalTaskId)
                throw new SubjectIdentificationSelectionConflictException(notificationId);

            selection.CanonicalTaskId = canonicalTaskId;
            selection.CreatedTask = createdTask;
            selection.ReusedTask = reusedTask;
            var failure = await db.FailureNotifications.SingleOrDefaultAsync(
                x => x.NotificationId == notificationId, cancellationToken).ConfigureAwait(false);
            if (failure is not null)
            {
                failure.ResolutionStatus = CollectionFailureResolutionStatus.RecoveryInProgress;
                failure.RecoveryTaskId = canonicalTaskId;
                failure.RecoveryStartedAt ??= finalizedAt;
            }
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return ToApplication(selection, notificationId);
        }
        finally { _gate.Release(); }
    }

    private static bool CandidateEquals(SubjectIdentificationCandidate stored,
        SubjectIdentificationCandidate submitted)
        => string.Equals(stored.Name, submitted.Name, StringComparison.Ordinal)
           && string.Equals(JraSourceIdentity.NormalizeHorseUrl(stored.Url)?.AbsoluteUri,
               JraSourceIdentity.NormalizeHorseUrl(submitted.Url)?.AbsoluteUri, StringComparison.Ordinal)
           && string.Equals(stored.Evidence, submitted.Evidence, StringComparison.Ordinal);

    private static SubjectIdentificationCandidateApplication ValidateExistingSelection(
        SubjectIdentificationSelectionEntity existing, Guid notificationId,
        SubjectIdentificationCandidate candidate, ResourceKey canonicalResource,
        CollectionDefinitionId canonicalDefinition, Uri normalizedUrl)
    {
        if (!string.Equals(existing.SelectedUrl, normalizedUrl.AbsoluteUri, StringComparison.Ordinal)
            || !string.Equals(existing.SelectedName, candidate.Name, StringComparison.Ordinal)
            || !string.Equals(existing.Evidence, candidate.Evidence, StringComparison.Ordinal)
            || existing.ResourceType != canonicalResource.Type
            || !string.Equals(existing.Provider, canonicalResource.Provider, StringComparison.Ordinal)
            || !string.Equals(existing.ResourceId, canonicalResource.Id, StringComparison.Ordinal)
            || !string.Equals(existing.DefinitionId, canonicalDefinition.Value, StringComparison.Ordinal))
            throw new SubjectIdentificationSelectionConflictException(notificationId);
        return ToApplication(existing, notificationId);
    }

    private static SubjectIdentificationCandidateApplication ToApplication(
        SubjectIdentificationSelectionEntity selection, Guid notificationId)
        => new(notificationId, selection.SelectedName, new Uri(selection.SelectedUrl), selection.Evidence,
            new(selection.ResourceType, selection.Provider, selection.ResourceId),
            new(selection.DefinitionId), selection.CanonicalTaskId, selection.Selector,
            selection.SelectedAt, selection.CreatedTask, selection.ReusedTask);
}
