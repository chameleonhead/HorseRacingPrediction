using System.Text.Json;
using System.Text;
using HorseRacingPrediction.Contracts.Identity;
using Microsoft.Data.Sqlite;

namespace HorseRacingPrediction.Api.Identity;

internal sealed class IdentityMigrationPreflightService(
    IdentityMigrationPreflightEventStoreReader eventStoreReader,
    IdentityMigrationPreflightConfiguredSurfaceReader configuredSurfaceReader,
    IdentityMigrationPreflightCursorProtector cursorProtector,
    TimeProvider timeProvider)
{
    private const int MaximumResponseBytes = 1024 * 1024;
    private static readonly SemaphoreSlim Admission = new(1, 1);
    private static readonly JsonSerializerOptions ResponseJson = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = IdentityMigrationPreflightPolicy.MaximumJsonDepth,
    };

    internal async Task<IdentityMigrationPreflightPage> GetPageAsync(string? cursor,
        CancellationToken requestCancellationToken)
    {
        if (cursor is not null && (cursor.Length > IdentityMigrationPreflightPolicy.MaximumCursorCharacters
            || Encoding.UTF8.GetByteCount(cursor) > IdentityMigrationPreflightPolicy.MaximumCursorCharacters))
            throw new IdentityMigrationPreflightInvalidCursorException();

        if (!await Admission.WaitAsync(0, requestCancellationToken).ConfigureAwait(false))
            throw new IdentityMigrationPreflightBusyException();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(requestCancellationToken);
        deadline.CancelAfter(IdentityMigrationPreflightPolicy.RequestDeadline);
        try
        {
            IdentityMigrationPreflightCursor? unprotected = null;
            if (cursor is not null
                && (!cursorProtector.TryUnprotectAuthenticated(cursor, out unprotected) || unprotected is null))
                throw new IdentityMigrationPreflightInvalidCursorException();

            var eventStoreSchema = await eventStoreReader.InspectSchemaAsync(deadline.Token).ConfigureAwait(false);
            var configuredSchemas = await configuredSurfaceReader.InspectAsync(deadline.Token).ConfigureAwait(false);
            var schemas = new[] { eventStoreSchema }.Concat(configuredSchemas).ToArray();
            var schemaAssumptions = string.Join(";", schemas.OrderBy(item => item.Surface)
                .Select(item => $"{item.Surface}:{item.Status}:{item.AssumptionFingerprint}"));
            if (schemaAssumptions.Length > IdentityMigrationPreflightPolicy.MaximumSchemaAssumptionsCharacters)
                return IncompleteSchemaPage(schemas);

            var phase = unprotected?.Phase ?? IdentityMigrationPreflightPolicy.GetPhases()[0];
            var afterKey = unprotected?.Key;

            var requiredSurface = phase == IdentityMigrationPreflightPhase.CollectionResources
                ? IdentityMigrationPreflightSurface.CollectionPlatform
                : IdentityMigrationPreflightSurface.EventStore;
            var requiredObservation = schemas.SingleOrDefault(item => item.Surface == requiredSurface);
            if (requiredObservation?.Status == IdentityMigrationPreflightObservationStatus.Unavailable)
                throw new IdentityMigrationPreflightUnavailableException();
            if (unprotected is not null
                && !string.Equals(unprotected.SchemaAssumptions, schemaAssumptions, StringComparison.Ordinal))
                throw new IdentityMigrationPreflightInvalidCursorException();
            if (requiredObservation is null || requiredObservation.Status is
                IdentityMigrationPreflightObservationStatus.Unsupported or
                IdentityMigrationPreflightObservationStatus.Truncated)
                return IncompleteStorePage(phase, schemas);

            var rows = phase == IdentityMigrationPreflightPhase.CollectionResources
                ? await ReadCollectionPageAsync(afterKey, deadline.Token).ConfigureAwait(false)
                : await eventStoreReader.ReadPhaseAsync(phase, afterKey, null, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();

            var nextPhase = rows.NoSafeContinuation ? null
                : rows.HasMore ? phase : IdentityMigrationPreflightPolicy.GetNextPhase(phase);
            var nextKey = rows.HasMore ? rows.LastKey : null;
            string? nextCursor = null;
            var cursorOverflow = false;
            if (nextPhase is not null)
            {
                try
                {
                    nextCursor = cursorProtector.Protect(new(
                        IdentityMigrationPreflightPolicy.PolicyVersion,
                        IdentityMigrationPreflightPolicy.StoreSelection,
                        IdentityMigrationPreflightPolicy.SchemaAssumptionVersion,
                        schemaAssumptions,
                        nextPhase.Value,
                        nextKey ?? string.Empty,
                        timeProvider.GetUtcNow().Add(IdentityMigrationPreflightPolicy.CursorLifetime)));
                }
                catch (ArgumentException)
                {
                    cursorOverflow = true;
                }
            }

            var state = nextPhase is null
                ? IdentityMigrationPreflightPageState.Complete
                : IdentityMigrationPreflightPageState.InProgress;
            if (cursorOverflow || rows.NoSafeContinuation || rows.ContainsTruncation || rows.UnknownIdentifierCount > 0
                || rows.Evidence.Any(item => item.Status is IdentityMigrationPreflightObservationStatus.UnknownShape
                    or IdentityMigrationPreflightObservationStatus.Conflict
                    or IdentityMigrationPreflightObservationStatus.Truncated
                    or IdentityMigrationPreflightObservationStatus.Unverifiable)
                || schemas.Any(item => item.Status is IdentityMigrationPreflightObservationStatus.UnknownShape
                    or IdentityMigrationPreflightObservationStatus.Unsupported
                    or IdentityMigrationPreflightObservationStatus.Unavailable
                    or IdentityMigrationPreflightObservationStatus.Truncated))
                state = IdentityMigrationPreflightPageState.Incomplete;

            if (cursorOverflow || rows.NoSafeContinuation) nextCursor = null;
            var page = new IdentityMigrationPreflightPage(
                state,
                phase,
                rows.RowsInspected == 0 ? null : timeProvider.GetUtcNow(),
                rows.RowsInspected,
                BuildCounts(rows, schemas, cursorOverflow),
                schemas.Select(item => item.ToContract()).ToArray(),
                rows.Evidence,
                nextCursor,
                IsNonAtomic: true);

            if (JsonSerializer.SerializeToUtf8Bytes(page, ResponseJson).Length <= MaximumResponseBytes)
                return page;

            return new(
                IdentityMigrationPreflightPageState.Incomplete,
                phase,
                page.ObservedAtUtc,
                0,
                page.Counts with { Truncated = page.Counts.Truncated + Math.Max(1, rows.RowsInspected) },
                page.Surfaces,
                [],
                null,
                IsNonAtomic: true);
        }
        catch (OperationCanceledException) when (!requestCancellationToken.IsCancellationRequested)
        {
            throw new IdentityMigrationPreflightDeadlineException();
        }
        catch (SqliteException)
        {
            throw new IdentityMigrationPreflightUnavailableException();
        }
        finally
        {
            Admission.Release();
        }
    }

    private async Task<IdentityMigrationPreflightScannedRows> ReadCollectionPageAsync(string? afterKey,
        CancellationToken cancellationToken)
    {
        var collection = await configuredSurfaceReader.ReadCollectionResourcesAsync(afterKey, cancellationToken)
            .ConfigureAwait(false);
        var eventEvidence = await eventStoreReader.ReadCollectionPhaseEvidenceAsync(collection.Keys,
            collection.Sources, cancellationToken).ConfigureAwait(false);
        return eventEvidence with
        {
            RowsInspected = eventEvidence.RowsInspected + collection.OverlongCount,
            UnknownIdentifierCount = eventEvidence.UnknownIdentifierCount + collection.OverlongCount,
            HasMore = collection.HasMore,
            NoSafeContinuation = collection.NoSafeContinuation,
            LastKey = collection.NoSafeContinuation ? null : collection.Keys.Count == 0 ? afterKey : collection.Keys[^1],
            ContainsTruncation = eventEvidence.ContainsTruncation || collection.OverlongCount > 0
                || collection.Sources.Values.Any(source => source.Truncated),
        };
    }

    private static IdentityMigrationPreflightCounts BuildCounts(IdentityMigrationPreflightScannedRows rows,
        IReadOnlyList<IdentityMigrationPreflightSchemaObservation> schemas, bool cursorOverflow)
    {
        var values = Enum.GetValues<IdentityMigrationPreflightObservationStatus>()
            .ToDictionary(status => status, _ => 0);
        foreach (var evidence in rows.Evidence) values[evidence.Status]++;
        values[IdentityMigrationPreflightObservationStatus.UnknownShape] += rows.UnknownIdentifierCount;
        foreach (var schema in schemas) values[schema.Status]++;
        if (cursorOverflow) values[IdentityMigrationPreflightObservationStatus.Truncated]++;
        else if (rows.ContainsTruncation && rows.Evidence.All(item =>
                     item.Status != IdentityMigrationPreflightObservationStatus.Truncated))
            values[IdentityMigrationPreflightObservationStatus.Truncated]++;
        return new(
            values[IdentityMigrationPreflightObservationStatus.Observed],
            values[IdentityMigrationPreflightObservationStatus.SchemaOnly],
            values[IdentityMigrationPreflightObservationStatus.Unsupported],
            values[IdentityMigrationPreflightObservationStatus.UnknownShape],
            values[IdentityMigrationPreflightObservationStatus.Truncated],
            values[IdentityMigrationPreflightObservationStatus.Unavailable],
            values[IdentityMigrationPreflightObservationStatus.NotConfigured],
            values[IdentityMigrationPreflightObservationStatus.NotInspected],
            values[IdentityMigrationPreflightObservationStatus.Unverifiable],
            values[IdentityMigrationPreflightObservationStatus.Conflict]);
    }

    private static IdentityMigrationPreflightPage IncompleteSchemaPage(
        IReadOnlyList<IdentityMigrationPreflightSchemaObservation> schemas) => new(
        IdentityMigrationPreflightPageState.Incomplete,
        IdentityMigrationPreflightPhase.HorseRoots,
        null,
        0,
        new(0, 0, 0, 0, 1, 0, 0, 0, 0, 0),
        schemas.Select(item => item.ToContract()).ToArray(),
        [],
        null,
        IsNonAtomic: true);

    private static IdentityMigrationPreflightPage IncompleteStorePage(
        IdentityMigrationPreflightPhase phase,
        IReadOnlyList<IdentityMigrationPreflightSchemaObservation> schemas) => new(
        IdentityMigrationPreflightPageState.Incomplete,
        phase,
        null,
        0,
        BuildCounts(new(0, 0, [], false, null), schemas, cursorOverflow: false),
        schemas.Select(item => item.ToContract()).ToArray(),
        [],
        null,
        IsNonAtomic: true);
}

internal sealed class IdentityMigrationPreflightBusyException : Exception { }
internal sealed class IdentityMigrationPreflightInvalidCursorException : Exception { }
internal sealed class IdentityMigrationPreflightDeadlineException : Exception { }
internal sealed class IdentityMigrationPreflightUnavailableException : Exception { }
