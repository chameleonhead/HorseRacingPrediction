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
    private const string TargetReviewKind = "TargetReviewV2";

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(PreviewPath, PreviewAsync);
        endpoints.MapGet(SchemaDiagnosticsPath, GetSchemaDiagnosticsAsync);
        endpoints.MapPost(OperationsPath, StageAsync);
        endpoints.MapPost(OperationsPath + "/{operationId}/execute", ExecuteAsync);
        endpoints.MapGet(OperationsPath + "/{operationId}", GetStatusAsync);
        endpoints.MapGet(RequestsPath + "/{requestKey}", GetRequestAsync);
    }

    private static async Task<IResult> PreviewAsync(
        HttpRequest request, HttpResponse response, [FromServices] TypedHorseIdentityStoreEngine engine,
        CancellationToken cancellationToken)
    {
        SetNoStore(response);
        using var deadline = CreateDeadline(cancellationToken);
        var inspectionProgress = new HorseIdentityMigrationInspectionProgress();
        var kinds = request.Query["kind"];
        if (kinds.Count > 1 || (kinds.Count == 1 && !string.Equals(kinds[0], TargetReviewKind, StringComparison.Ordinal)))
            return Error(StatusCodes.Status400BadRequest, "UnknownPreviewKind");
        try
        {
            var parsed = await ReadBodyAsync<HorseIdentityMigrationManifest>(request, deadline.Token).ConfigureAwait(false);
            if (!parsed.Success) return Error(parsed.StatusCode, parsed.ErrorCode!);
            if (!HorseIdentityMigrationManifestPolicy.Validate(parsed.Value).IsValid)
                return Error(StatusCodes.Status400BadRequest, "InvalidManifest");
            if (kinds.Count == 1)
            {
                HorseIdentityMigrationTargetReview review;
                try
                {
                    review = await engine.InspectTargetReviewAsync(parsed.Value, deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested
                    && !cancellationToken.IsCancellationRequested)
                {
                    review = new(false, [HorseIdentityMigrationTargetReviewBlocker.ReadUnavailable], [], []);
                }
                return Results.Ok(review);
            }
            var preview = await engine.InspectPreviewAsync(parsed.Value, deadline.Token, inspectionProgress)
                .ConfigureAwait(false);
            if (deadline.IsCancellationRequested)
                preview = DeadlinePreview(inspectionProgress, cancellationToken);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(preview, JsonOptions);
            if (bytes.Length > HorseIdentityMigrationPreview.MaximumSummaryBytes)
                preview = TypedHorseIdentityStoreEngine.BoundPreviewSummary(preview with
                {
                    SchemaDiagnostics = preview.SchemaDiagnostics is null
                        ? null
                        : TypedHorseIdentityStoreReaders.IncompleteSchemaDiagnostics(preview.SchemaDiagnostics),
                });
            if (deadline.IsCancellationRequested)
                preview = DeadlinePreview(inspectionProgress, cancellationToken);
            return Results.Ok(preview);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            return Results.Ok(DeadlinePreview(inspectionProgress, cancellationToken));
        }
    }

    internal static HorseIdentityMigrationPreview DeadlinePreview(
        HorseIdentityMigrationInspectionProgress inspectionProgress,
        CancellationToken callerCancellationToken = default)
    {
        callerCancellationToken.ThrowIfCancellationRequested();
        return TypedHorseIdentityStoreEngine.CreateIncompletePreview() with
        {
            InspectionDiagnostic = inspectionProgress.Snapshot(HorseIdentityMigrationInspectionResult.DeadlineExceeded),
        };
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
        var requestKind = body?.RequestKind ?? HorseIdentityMigrationAdmissionKind.BoundV1;
        if (body?.Manifest is null || !HorseIdentityMigrationExecutionPolicy.IsDefinedAdmissionKind(body.RequestKind)
            || !Enum.IsDefined(requestKind)
            || !HorseIdentityMigrationExecutionPolicy.IsValidRequestKey(body.RequestKey)
            || (requestKind == HorseIdentityMigrationAdmissionKind.BoundV1
                && (string.IsNullOrWhiteSpace(body.PreviewFingerprint) || body.PreviewFingerprint.Length != 64
                    || !body.PreviewFingerprint.All(Uri.IsHexDigit)))
            || (requestKind == HorseIdentityMigrationAdmissionKind.PendingV2 && body.PreviewFingerprint is not null))
            return Error(StatusCodes.Status400BadRequest, "InvalidRequest");

        var validation = HorseIdentityMigrationManifestPolicy.Validate(body.Manifest);
        if (!validation.IsValid) return Error(StatusCodes.Status400BadRequest, "InvalidManifest");

        var immutableRequestHash = requestKind == HorseIdentityMigrationAdmissionKind.PendingV2
            ? HorseIdentityMigrationExecutionPolicy.ComputePendingImmutableRequestHash(body.Manifest)
            : HorseIdentityMigrationExecutionPolicy.ComputeImmutableRequestHash(body.Manifest, body.PreviewFingerprint!);

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
                    return prior.RequestKind == requestKind
                        && string.Equals(prior.ImmutableRequestHash, immutableRequestHash, StringComparison.Ordinal)
                        ? Results.Ok(HorseIdentityMigrationPublicMapper.ToPublic(prior.Operation) with
                        {
                            SavedManifest = body.Manifest,
                            PreviewFingerprint = prior.PreviewFingerprint,
                            RequestKind = prior.RequestKind,
                        })
                        : Error(StatusCodes.Status409Conflict, "RequestConflict");
            }

            if (requestKind == HorseIdentityMigrationAdmissionKind.PendingV2)
            {
                HorseIdentityMigrationTargetReview targetReviewFresh;
                try
                {
                    targetReviewFresh = await engine.InspectTargetReviewAsync(body.Manifest, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (IsUnavailable(exception) && body.RequestKey is { } failedRequestKey)
                {
                    var replay = await WaitForRequestReplayAsync(journal, failedRequestKey, immutableRequestHash,
                        requestKind, body.Manifest, body.PreviewFingerprint, cancellationToken).ConfigureAwait(false);
                    if (replay is not null) return replay;
                    throw;
                }

                if (!targetReviewFresh.CanSavePending)
                {
                    if (targetReviewFresh.Blockers.Contains(HorseIdentityMigrationTargetReviewBlocker.ReadUnavailable))
                        return Error(StatusCodes.Status503ServiceUnavailable, "Unavailable");
                    if (targetReviewFresh.Blockers.Contains(HorseIdentityMigrationTargetReviewBlocker.InvalidManifest))
                        return Error(StatusCodes.Status400BadRequest, "InvalidManifest");
                    return Error(StatusCodes.Status409Conflict, "TargetReviewStale");
                }

                var pendingOperationId = Guid.NewGuid();
                var pendingRequestKey = body.RequestKey ?? Guid.NewGuid();
                var pendingOutcome = await journal.StagePendingWithRequestKeyAsync(pendingOperationId,
                    body.Manifest, pendingRequestKey, immutableRequestHash, timeProvider.GetUtcNow(), cancellationToken)
                    .ConfigureAwait(false);
                if (!pendingOutcome.Succeeded || pendingOutcome.Operation is null)
                    return Error(pendingOutcome.Reason switch
                    {
                        HorseIdentityMigrationJournalReason.RequestConflict => StatusCodes.Status409Conflict,
                        HorseIdentityMigrationJournalReason.CapacityExceeded or HorseIdentityMigrationJournalReason.OwnershipBusy
                            => StatusCodes.Status429TooManyRequests,
                        _ => StatusCodes.Status503ServiceUnavailable,
                    }, pendingOutcome.Reason switch
                    {
                        HorseIdentityMigrationJournalReason.RequestConflict => "RequestConflict",
                        HorseIdentityMigrationJournalReason.CapacityExceeded => "CapacityExceeded",
                        HorseIdentityMigrationJournalReason.OwnershipBusy => "Busy",
                        _ => "Unavailable",
                    });

                var pendingResponse = HorseIdentityMigrationPublicMapper.ToPublic(pendingOutcome.Operation) with
                {
                    SavedManifest = body.Manifest,
                    RequestKind = requestKind,
                };
                return pendingOutcome.Operation.OperationId == pendingOperationId
                    ? Results.Created($"/api/v2/admin/horse-id-migration-operations/{pendingOperationId:D}", pendingResponse)
                    : Results.Ok(pendingResponse);
            }

            HorseIdentityMigrationPreview fresh;
            try
            {
                fresh = await engine.InspectPreviewAsync(body.Manifest, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsUnavailable(exception) && body.RequestKey is { } failedRequestKey)
            {
                var replay = await WaitForRequestReplayAsync(journal, failedRequestKey, immutableRequestHash,
                    requestKind, body.Manifest, body.PreviewFingerprint, cancellationToken).ConfigureAwait(false);
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
                            immutableRequestHash, requestKind, body.Manifest, body.PreviewFingerprint,
                            cancellationToken).ConfigureAwait(false);
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
            // The API always stores the preview fingerprint with its staged operation so
            // a later explicit Execute (including after reload) is bound to the exact
            // displayed request. A caller-supplied key remains the idempotency key; when
            // omitted, this private journal key is not exposed as a public capability.
            var requestKey = body.RequestKey ?? Guid.NewGuid();
            var outcome = await journal.StageWithRequestKeyAsync(operationId, body.Manifest, requestKey,
                immutableRequestHash, body.PreviewFingerprint, timeProvider.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
            if (!outcome.Succeeded || outcome.Operation is null)
            {
                if (outcome.Reason == HorseIdentityMigrationJournalReason.OwnershipBusy
                    && body.RequestKey is { } busyRequestKey)
                {
                    var replay = await WaitForRequestReplayAsync(journal, busyRequestKey, immutableRequestHash,
                        requestKind, body.Manifest, body.PreviewFingerprint, cancellationToken).ConfigureAwait(false);
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

            var operationResponse = HorseIdentityMigrationPublicMapper.ToPublic(outcome.Operation) with
            {
                SavedManifest = body.Manifest,
                PreviewFingerprint = body.PreviewFingerprint,
                RequestKind = requestKind,
            };
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
                    requestKind, body.Manifest, body.PreviewFingerprint, cancellationToken).ConfigureAwait(false);
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
            if (operation is null) return Results.NotFound();
            var saved = await journal.GetSavedRequestAsync(parsed, cancellationToken).ConfigureAwait(false);
            var pending = saved is null
                ? await journal.GetPendingRequestAsync(parsed, cancellationToken).ConfigureAwait(false)
                : null;
            var intent = await journal.GetExecutionIntentAsync(parsed, cancellationToken).ConfigureAwait(false);
            return Results.Ok(ToPublic(operation, saved, pending, intent));
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
            if (record is null) return Results.NotFound();
            var saved = record.RequestKind == HorseIdentityMigrationAdmissionKind.BoundV1
                ? await journal.GetSavedRequestAsync(record.Operation.OperationId, cancellationToken).ConfigureAwait(false)
                : null;
            var pending = record.RequestKind == HorseIdentityMigrationAdmissionKind.PendingV2
                ? await journal.GetPendingRequestAsync(record.Operation.OperationId, cancellationToken).ConfigureAwait(false)
                : null;
            return Results.Ok(HorseIdentityMigrationPublicMapper.ToPublic(record.Operation) with
            {
                SavedManifest = saved?.Manifest ?? pending?.Manifest,
                PreviewFingerprint = saved?.PreviewFingerprint,
                RequestKind = record.RequestKind,
            });
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

    private static async Task<IResult> ExecuteAsync(
        string operationId, HttpResponse response,
        IServiceProvider services,
        [FromServices] HorseIdentityMigrationJournal journal,
        CancellationToken cancellationToken)
    {
        SetNoStore(response);
        if (!Guid.TryParseExact(operationId, "D", out var parsed) || parsed == Guid.Empty)
            return Results.NotFound();

        var preparation = services.GetService(typeof(HorseIdentityMigrationPreparationService))
            as HorseIdentityMigrationPreparationService;
        if (preparation is null || !preparation.IsEnabled)
            return Error(StatusCodes.Status503ServiceUnavailable, "Unavailable");

        try
        {
            var result = await preparation.RequestExecutionAsync(parsed, cancellationToken).ConfigureAwait(false);
            if (result != HorseIdentityMigrationExecutionRequestResult.Accepted)
                return result switch
                {
                    HorseIdentityMigrationExecutionRequestResult.NotFound => Results.NotFound(),
                    HorseIdentityMigrationExecutionRequestResult.Unavailable =>
                        Error(StatusCodes.Status503ServiceUnavailable, "Unavailable"),
                    _ => Error(StatusCodes.Status409Conflict, "Conflict"),
                };

            var operation = await journal.GetStatusAsync(parsed, CancellationToken.None).ConfigureAwait(false);
            if (operation is null) return Error(StatusCodes.Status503ServiceUnavailable, "Unavailable");
            var saved = await journal.GetSavedRequestAsync(parsed, CancellationToken.None).ConfigureAwait(false);
            var pending = saved is null
                ? await journal.GetPendingRequestAsync(parsed, CancellationToken.None).ConfigureAwait(false)
                : null;
            var intent = await journal.GetExecutionIntentAsync(parsed, CancellationToken.None).ConfigureAwait(false);
            return Results.Accepted($"/api/v2/admin/horse-id-migration-operations/{parsed:D}",
                ToPublic(operation, saved, pending, intent));
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

    internal static HorseIdentityMigrationOperationResponse ToPublic(
        HorseIdentityMigrationJournalOperation operation, HorseIdentityMigrationSavedRequest? saved,
        HorseIdentityMigrationPendingRequest? pending = null,
        HorseIdentityMigrationExecutionIntent? intent = null) =>
        HorseIdentityMigrationPublicMapper.ToPublic(operation) with
        {
            SavedManifest = saved?.Manifest ?? pending?.Manifest,
            PreviewFingerprint = saved?.PreviewFingerprint,
            RequestKind = saved is not null ? HorseIdentityMigrationAdmissionKind.BoundV1
                : pending is not null ? HorseIdentityMigrationAdmissionKind.PendingV2 : null,
            ExecutionPhase = intent?.Phase.ToString(),
            ExecutionUpdatedAt = intent?.UpdatedAt,
        };

    // Keep the existing internal call shape used by the standalone supervisor.
    internal static HorseIdentityMigrationOperationResponse ToPublic(
        HorseIdentityMigrationJournalOperation operation, HorseIdentityMigrationSavedRequest? saved,
        HorseIdentityMigrationExecutionIntent? intent) => ToPublic(operation, saved, pending: null, intent);

    private static async Task<IResult?> WaitForRequestReplayAsync(
        HorseIdentityMigrationJournal journal,
        Guid requestKey,
        string immutableRequestHash,
        HorseIdentityMigrationAdmissionKind requestKind,
        HorseIdentityMigrationManifest manifest,
        string? previewFingerprint,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                var prior = await journal.GetByRequestKeyAsync(requestKey, cancellationToken).ConfigureAwait(false);
                if (prior is not null)
                    return prior.RequestKind == requestKind
                        && string.Equals(prior.ImmutableRequestHash, immutableRequestHash, StringComparison.Ordinal)
                        ? Results.Ok(HorseIdentityMigrationPublicMapper.ToPublic(prior.Operation) with
                        {
                            SavedManifest = manifest,
                            PreviewFingerprint = prior.PreviewFingerprint ?? previewFingerprint,
                            RequestKind = prior.RequestKind,
                        })
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
