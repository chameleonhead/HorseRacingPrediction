using System.Text.Json;
using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.Api.IdentityMigration;
using HorseRacingPrediction.Contracts.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace HorseRacingPrediction.Api.Endpoints.IdentityMigration;

internal static class HorseIdentityMigrationEndpoints
{
    private const int MaximumBodyBytes = HorseIdentityMigrationJournal.MaximumRequestBytes;
    private static readonly TimeSpan MaximumDiagnosticDuration = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string PreviewPath = "/v2/admin/horse-id-migration-previews";
    private const string OperationsPath = "/v2/admin/horse-id-migration-operations";
    private const string RequestsPath = "/v2/admin/horse-id-migration-requests";
    private const string SchemaDiagnosticsPath = "/v2/admin/horse-id-migration-schema-diagnostics";

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(PreviewPath, PreviewAsync);
        endpoints.MapGet(SchemaDiagnosticsPath, GetSchemaDiagnosticsAsync);
        endpoints.MapPost(OperationsPath, StageAsync);
        endpoints.MapGet(OperationsPath + "/{operationId}", GetStatusAsync);
        endpoints.MapGet(RequestsPath + "/{requestKey}", GetRequestAsync);
    }

    private static async Task<IResult> PreviewAsync(
        HttpRequest request, HttpResponse response, [FromServices] TypedHorseIdentityStoreEngine engine,
        CancellationToken cancellationToken)
    {
        SetNoStore(response);
        using var deadline = CreateDeadline(cancellationToken);
        try
        {
            var parsed = await ReadBodyAsync<HorseIdentityMigrationManifest>(request, deadline.Token).ConfigureAwait(false);
            if (!parsed.Success) return Error(parsed.StatusCode, parsed.ErrorCode!);
            if (!HorseIdentityMigrationManifestPolicy.Validate(parsed.Value).IsValid)
                return Error(StatusCodes.Status400BadRequest, "InvalidManifest");
            var preview = await engine.InspectPreviewAsync(parsed.Value, deadline.Token).ConfigureAwait(false);
            if (deadline.IsCancellationRequested)
                preview = TypedHorseIdentityStoreEngine.CreateIncompletePreview();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(preview, JsonOptions);
            if (bytes.Length > HorseIdentityMigrationPreview.MaximumSummaryBytes)
                preview = TypedHorseIdentityStoreEngine.BoundPreviewSummary(preview with
                {
                    SchemaDiagnostics = preview.SchemaDiagnostics is null
                        ? null
                        : TypedHorseIdentityStoreReaders.IncompleteSchemaDiagnostics(preview.SchemaDiagnostics),
                });
            if (deadline.IsCancellationRequested)
                preview = TypedHorseIdentityStoreEngine.CreateIncompletePreview();
            return Results.Ok(preview);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            return Results.Ok(TypedHorseIdentityStoreEngine.CreateIncompletePreview());
        }
    }

    private static async Task<IResult> GetSchemaDiagnosticsAsync(
        HttpResponse response, [FromServices] TypedHorseIdentityStoreEngine engine,
        CancellationToken cancellationToken)
    {
        SetNoStore(response);
        using var deadline = CreateDeadline(cancellationToken);
        HorseIdentityMigrationSchemaDiagnostics report;
        try
        {
            report = await engine.InspectSchemaDiagnosticsAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            report = TypedHorseIdentityStoreEngine.CreateIncompleteSchemaDiagnostics();
        }

        if (deadline.IsCancellationRequested)
            report = TypedHorseIdentityStoreEngine.CreateIncompleteSchemaDiagnostics();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions);
        if (bytes.Length > HorseIdentityMigrationSchemaDiagnostics.MaximumReportBytes)
        {
            report = TypedHorseIdentityStoreReaders.IncompleteSchemaDiagnostics(report);
            bytes = JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions);
        }
        if (deadline.IsCancellationRequested)
        {
            report = TypedHorseIdentityStoreEngine.CreateIncompleteSchemaDiagnostics();
            bytes = JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions);
        }
        return Results.Bytes(bytes, "application/json");
    }

    private static CancellationTokenSource CreateDeadline(CancellationToken requestToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
        source.CancelAfter(MaximumDiagnosticDuration);
        return source;
    }

    private static async Task<IResult> StageAsync(
        HttpRequest request, HttpResponse response, [FromServices] TypedHorseIdentityStoreEngine engine,
        [FromServices] HorseIdentityMigrationJournal journal, [FromServices] TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        SetNoStore(response);
        var parsed = await ReadBodyAsync<HorseIdentityMigrationStageRequest>(request, cancellationToken).ConfigureAwait(false);
        if (!parsed.Success) return Error(parsed.StatusCode, parsed.ErrorCode!);
        var body = parsed.Value;
        if (body?.Manifest is null || string.IsNullOrWhiteSpace(body.PreviewFingerprint)
            || body.PreviewFingerprint.Length != 64
            || !body.PreviewFingerprint.All(Uri.IsHexDigit)
            || !HorseIdentityMigrationExecutionPolicy.IsValidRequestKey(body.RequestKey))
            return Error(StatusCodes.Status400BadRequest, "InvalidRequest");

        var validation = HorseIdentityMigrationManifestPolicy.Validate(body.Manifest);
        if (!validation.IsValid) return Error(StatusCodes.Status400BadRequest, "InvalidManifest");

        var immutableRequestHash = HorseIdentityMigrationExecutionPolicy
            .ComputeImmutableRequestHash(body.Manifest, body.PreviewFingerprint);

        try
        {
            if (body.RequestKey is { } existingRequestKey)
            {
                HorseIdentityMigrationRequestKeyRecord? prior;
                try
                {
                    prior = await journal.GetByRequestKeyAsync(existingRequestKey, cancellationToken).ConfigureAwait(false);
                }
                catch (SqliteException)
                {
                    prior = null;
                }
                if (prior is not null)
                    return string.Equals(prior.ImmutableRequestHash, immutableRequestHash, StringComparison.Ordinal)
                        ? Results.Ok(HorseIdentityMigrationPublicMapper.ToPublic(prior.Operation))
                        : Error(StatusCodes.Status409Conflict, "RequestConflict");
            }

            HorseIdentityMigrationPreview fresh;
            try
            {
                fresh = await engine.InspectPreviewAsync(body.Manifest, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsUnavailable(exception) && body.RequestKey is { } failedRequestKey)
            {
                var replay = await WaitForRequestReplayAsync(journal, failedRequestKey, immutableRequestHash,
                    cancellationToken).ConfigureAwait(false);
                if (replay is not null) return replay;
                throw;
            }
            if (!fresh.CanStage || fresh.Fingerprint is null
                || !string.Equals(fresh.Fingerprint, body.PreviewFingerprint, StringComparison.Ordinal))
            {
                if (fresh.Blockers.Contains(HorseIdentityMigrationPreviewBlocker.ReadUnavailable)
                    || fresh.Blockers.Contains(HorseIdentityMigrationPreviewBlocker.StoreUnavailable))
                {
                    if (body.RequestKey is { } unavailableRequestKey)
                    {
                        var replay = await WaitForRequestReplayAsync(journal, unavailableRequestKey,
                            immutableRequestHash, cancellationToken).ConfigureAwait(false);
                        if (replay is not null) return replay;
                    }
                    return Error(StatusCodes.Status503ServiceUnavailable, "Unavailable");
                }
                if (fresh.Blockers.Contains(HorseIdentityMigrationPreviewBlocker.SummaryTooLarge))
                    return Error(StatusCodes.Status429TooManyRequests, "CapacityExceeded");
                if (fresh.Blockers.Contains(HorseIdentityMigrationPreviewBlocker.InvalidManifest))
                    return Error(StatusCodes.Status400BadRequest, "InvalidManifest");
                return Error(StatusCodes.Status409Conflict, "PreviewStale");
            }

            var operationId = Guid.NewGuid();
            var outcome = body.RequestKey is { } requestKey
                ? await journal.StageWithRequestKeyAsync(operationId, body.Manifest, requestKey,
                    immutableRequestHash, body.PreviewFingerprint, timeProvider.GetUtcNow(), cancellationToken)
                    .ConfigureAwait(false)
                : await journal.StageAsync(operationId, body.Manifest, timeProvider.GetUtcNow(), cancellationToken)
                    .ConfigureAwait(false);
            if (!outcome.Succeeded || outcome.Operation is null)
            {
                if (outcome.Reason == HorseIdentityMigrationJournalReason.OwnershipBusy
                    && body.RequestKey is { } busyRequestKey)
                {
                    var replay = await WaitForRequestReplayAsync(journal, busyRequestKey, immutableRequestHash,
                        cancellationToken).ConfigureAwait(false);
                    if (replay is not null) return replay;
                }
                return Error(outcome.Reason switch
                {
                    HorseIdentityMigrationJournalReason.RequestConflict => StatusCodes.Status409Conflict,
                    HorseIdentityMigrationJournalReason.CapacityExceeded or HorseIdentityMigrationJournalReason.OwnershipBusy
                        => StatusCodes.Status429TooManyRequests,
                    _ => StatusCodes.Status503ServiceUnavailable,
                }, outcome.Reason switch
                {
                    HorseIdentityMigrationJournalReason.RequestConflict => "RequestConflict",
                    HorseIdentityMigrationJournalReason.CapacityExceeded => "CapacityExceeded",
                    HorseIdentityMigrationJournalReason.OwnershipBusy => "Busy",
                    _ => "Unavailable",
                });
            }

            var operationResponse = HorseIdentityMigrationPublicMapper.ToPublic(outcome.Operation);
            return outcome.Operation.OperationId == operationId
                ? Results.Created($"/api/v2/admin/horse-id-migration-operations/{operationId:D}", operationResponse)
                : Results.Ok(operationResponse);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            if (body.RequestKey is { } failedRequestKey)
            {
                var replay = await WaitForRequestReplayAsync(journal, failedRequestKey, immutableRequestHash,
                    cancellationToken).ConfigureAwait(false);
                if (replay is not null) return replay;
            }
            return Error(StatusCodes.Status503ServiceUnavailable, "Unavailable");
        }
    }

    internal static async Task<IResult> GetStatusAsync(
        string operationId, HttpResponse response, [FromServices] HorseIdentityMigrationJournal journal,
        CancellationToken cancellationToken)
    {
        SetNoStore(response);
        if (!Guid.TryParseExact(operationId, "D", out var parsed) || parsed == Guid.Empty)
            return Results.NotFound();
        try
        {
            var operation = await journal.GetStatusAsync(parsed, cancellationToken).ConfigureAwait(false);
            return operation is null
                ? Results.NotFound()
                : Results.Ok(HorseIdentityMigrationPublicMapper.ToPublic(operation));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "Unavailable");
        }
    }

    internal static async Task<IResult> GetRequestAsync(
        string requestKey, HttpResponse response, [FromServices] HorseIdentityMigrationJournal journal,
        CancellationToken cancellationToken)
    {
        SetNoStore(response);
        if (!Guid.TryParseExact(requestKey, "D", out var parsed) || parsed == Guid.Empty)
            return Results.NotFound();
        try
        {
            var record = await journal.GetByRequestKeyAsync(parsed, cancellationToken).ConfigureAwait(false);
            return record is null
                ? Results.NotFound()
                : Results.Ok(HorseIdentityMigrationPublicMapper.ToPublic(record.Operation));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "Unavailable");
        }
    }

    private static async Task<BodyReadResult<T>> ReadBodyAsync<T>(HttpRequest request, CancellationToken token)
    {
        if (request.ContentLength is > MaximumBodyBytes)
            return BodyReadResult<T>.Failure(StatusCodes.Status429TooManyRequests, "CapacityExceeded");

        await using var buffer = new MemoryStream(Math.Min((int)(request.ContentLength ?? 0), MaximumBodyBytes));
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await request.Body.ReadAsync(chunk, token).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > MaximumBodyBytes)
                return BodyReadResult<T>.Failure(StatusCodes.Status429TooManyRequests, "CapacityExceeded");
            await buffer.WriteAsync(chunk.AsMemory(0, read), token).ConfigureAwait(false);
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)), JsonOptions);
            return value is null
                ? BodyReadResult<T>.Failure(StatusCodes.Status400BadRequest, "InvalidRequest")
                : BodyReadResult<T>.Ok(value);
        }
        catch (JsonException)
        {
            return BodyReadResult<T>.Failure(StatusCodes.Status400BadRequest, "InvalidRequest");
        }
    }

    private static IResult Error(int statusCode, string code) =>
        Results.Json(new HorseIdentityMigrationOperationError(code), statusCode: statusCode);

    private static async Task<IResult?> WaitForRequestReplayAsync(
        HorseIdentityMigrationJournal journal,
        Guid requestKey,
        string immutableRequestHash,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                var prior = await journal.GetByRequestKeyAsync(requestKey, cancellationToken).ConfigureAwait(false);
                if (prior is not null)
                    return string.Equals(prior.ImmutableRequestHash, immutableRequestHash, StringComparison.Ordinal)
                        ? Results.Ok(HorseIdentityMigrationPublicMapper.ToPublic(prior.Operation))
                        : Error(StatusCodes.Status409Conflict, "RequestConflict");
            }
            catch (SqliteException)
            {
                // The atomic journal admission may still be committing; retry within this bounded request window.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50 * (attempt + 1)), cancellationToken)
                .ConfigureAwait(false);
        }

        return null;
    }

    private static void SetNoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        response.Headers.Pragma = "no-cache";
    }

    private static bool IsUnavailable(Exception exception) => exception is IOException or UnauthorizedAccessException
        or InvalidDataException or SqliteException;

    private sealed record BodyReadResult<T>(bool Success, T? Value, int StatusCode, string? ErrorCode)
    {
        internal static BodyReadResult<T> Ok(T value) => new(true, value, 0, null);
        internal static BodyReadResult<T> Failure(int statusCode, string code) => new(false, default, statusCode, code);
    }
}
