using EventFlow.Aggregates;
using EventFlow.EventStores;
using EventFlow.Extensions;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Identity;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Infrastructure.Persistence;
using HorseRacingPrediction.PredictionScheduling;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using HorseRacingPrediction.Api.Web;
using HorseRacingPrediction.Api.Endpoints.IdentityMigration;
using System.Net;

namespace HorseRacingPrediction.Api.IdentityMigration;

internal enum HorseIdentityMigrationRuntimeModeKind
{
    NormalHost,
    InvalidCommand,
    ExecuteOperation,
}

internal sealed record HorseIdentityMigrationRuntimeModeSelection(
    HorseIdentityMigrationRuntimeModeKind Kind,
    Guid? OperationId = null);

/// <summary>Routes only the fixed offline migration command before the normal API host is composed.</summary>
internal static class HorseIdentityMigrationRuntimeMode
{
    internal const string CommandName = "horse-id-migration";
    internal const string StatusRoute = "/api/v2/admin/horse-id-migration-operations/{operationId}";
    internal const string OperatorOnlyEnvironmentVariable = "HRP_HORSE_IDENTITY_OPERATOR_ONLY";
    private static readonly TimeSpan OperatorStatusReadDeadline = TimeSpan.FromSeconds(2);

    internal static bool IsOperatorOnlyMode() => string.Equals(
        Environment.GetEnvironmentVariable(OperatorOnlyEnvironmentVariable), "true", StringComparison.Ordinal);

    internal static HorseIdentityMigrationRuntimeModeSelection Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 0 || !string.Equals(arguments[0], CommandName, StringComparison.Ordinal))
            return new(HorseIdentityMigrationRuntimeModeKind.NormalHost);
        if (arguments.Count != 2 || !Guid.TryParseExact(arguments[1], "D", out var operationId)
            || operationId == Guid.Empty)
            return new(HorseIdentityMigrationRuntimeModeKind.InvalidCommand);
        return new(HorseIdentityMigrationRuntimeModeKind.ExecuteOperation, operationId);
    }

    internal static async Task<int?> RunFixedCommandAsync(string[] arguments)
    {
        var selection = Parse(arguments);
        if (selection.Kind == HorseIdentityMigrationRuntimeModeKind.NormalHost) return null;
        if (selection.Kind == HorseIdentityMigrationRuntimeModeKind.InvalidCommand) return 2;
        try
        {
            var builder = WebApplication.CreateBuilder(Array.Empty<string>());
            var journal = CreateJournal(builder);
            var operationId = selection.OperationId!.Value;
            var prior = await journal.GetStatusAsync(operationId).ConfigureAwait(false);
            if (prior?.Status == HorseIdentityMigrationOperationStatus.Succeeded) return 0;

            var guard = await journal.InspectStartupGuardAsync(DateTimeOffset.UtcNow).ConfigureAwait(false);
            if (guard.State != HorseIdentityMigrationStartupState.Ready) return 3;

            if (prior is not null && prior.Status != HorseIdentityMigrationOperationStatus.Staged) return 4;

            var configuredSources = ResolveConfiguredSources(builder, ReadCollectionOptions(builder));
            HorseIdentityMigrationMaintenanceCapability.OperationAuthorization? operationAuthorization = null;
            var privateAuthorization = Environment.GetEnvironmentVariable(
                HorseIdentityMigrationMaintenanceCapability.PrivateEnvironmentVariable);
            if (privateAuthorization is null)
                return await RunStandaloneFixedCommandAsync(builder, journal, operationId, configuredSources)
                    .ConfigureAwait(false);

            var executableRequest = await journal.GetExecutableRequestAsync(operationId).ConfigureAwait(false);
            if (executableRequest is null) return 4;

            if (privateAuthorization is not null)
            {
                if (!HorseIdentityMigrationMaintenanceCapability.OperationAuthorization.TryImportAndValidate(
                        privateAuthorization, configuredSources.PhysicalSourcePaths, out operationAuthorization)
                    || !HorseIdentityMigrationSupervisor.TryEstablishParentDeathSignal(
                        operationAuthorization!.ParentProcessId, operationAuthorization.ParentStartTicks))
                    return 4;
                if (executableRequest.RequestKind == HorseIdentityMigrationAdmissionKind.PendingV2)
                {
                    var seal = await journal.GetPendingSealAsync(operationId).ConfigureAwait(false);
                    if (seal is null
                        || !string.Equals(seal.ImmutableRequestHash, executableRequest.ImmutableRequestHash,
                            StringComparison.Ordinal)
                        || !string.Equals(seal.SourceSetHash, operationAuthorization!.SourceSetHash,
                            StringComparison.Ordinal))
                        return 4;
                }
            }

            using var sourceOwnership = HorseIdentityMigrationRuntimeOwnership.AcquireForOperation(
                configuredSources.PhysicalSourcePaths, operationAuthorization!);

            string? authorizedBackupManifestHash = null;
            if (executableRequest.RequestKind == HorseIdentityMigrationAdmissionKind.PendingV2)
            {
                var seal = await journal.GetPendingSealAsync(operationId).ConfigureAwait(false);
                if (seal is null) return 4;
                var backupReceipt = await HorseIdentityMigrationManagedBackup.ForJournal(journal)
                    .ValidateAsync(operationId, configuredSources, journal).ConfigureAwait(false);
                if (!string.Equals(seal.BackupManifestHash, backupReceipt.ManifestHash, StringComparison.Ordinal))
                    return 4;
                authorizedBackupManifestHash = backupReceipt.ManifestHash;
            }

            var claim = await journal.ClaimWorkflowAsync(operationId, DateTimeOffset.UtcNow).ConfigureAwait(false);
            if (!claim.Succeeded) return 4;

            var operation = await journal.GetClaimedStagedRequestAsync(operationId).ConfigureAwait(false);
            if (operation is null) return 4;

            var (services, engine) = CreateStoreEngine(builder);
            using (services)
            {
                var result = await engine.ExecuteOwnedAsync(operation.OperationId, operation.Manifest, journal,
                    CancellationToken.None, authorizedSourceSetHash: operationAuthorization!.SourceSetHash,
                    authorizedBackupManifestHash: authorizedBackupManifestHash)
                    .ConfigureAwait(false);
                return result.Status == TypedHorseIdentityExecutionStatus.Completed && result.CanWrite ? 0 : 5;
            }
        }
        catch (OperationCanceledException) { return 5; }
        catch (Exception) { return 5; }
    }

    private static async Task<int> RunStandaloneFixedCommandAsync(WebApplicationBuilder builder,
        HorseIdentityMigrationJournal journal, Guid operationId, HorseIdentityMigrationConfiguredSources sources)
    {
        var paths = sources.PhysicalSourcePaths;
        using var controlLease = HorseIdentityMigrationMaintenanceOwnership.AcquireSupervisorControlLease(paths);
        using var runtimeOwnership = HorseIdentityMigrationRuntimeOwnership.Acquire(paths);

        var operation = await journal.GetStatusAsync(operationId).ConfigureAwait(false);
        if (operation?.Status != HorseIdentityMigrationOperationStatus.Staged) return 4;
        if (await journal.GetPendingRequestAsync(operationId).ConfigureAwait(false) is not null) return 4;
        if (await journal.GetExecutionIntentAsync(operationId).ConfigureAwait(false) is not null) return 4;
        if (!(await journal.ClaimWorkflowAsync(operationId, DateTimeOffset.UtcNow).ConfigureAwait(false)).Succeeded)
            return 4;
        var claimed = await journal.GetClaimedStagedRequestAsync(operationId).ConfigureAwait(false);
        if (claimed is null) return 4;

        var collectionOptions = ReadCollectionOptions(builder);
        var collectionStore = new CollectionPlatformStore(Options.Create(collectionOptions));
        var pause = await collectionStore.GetPipelineStateAsync().ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        if (!pause.IsPaused || pause.UpdatedAt is null
            || now < pause.UpdatedAt.Value + TimeSpan.FromSeconds(900)
            || (await collectionStore.GetTasksAsync(CollectionTaskStatus.Running, 1).ConfigureAwait(false)).Count != 0)
            return 4;

        using var services = new ServiceCollection().AddLogging().AddEventFlow(options => options
            .AddDefaults(typeof(HorseAggregate).Assembly)
            .AddDefaults(typeof(RaceAggregate).Assembly)).BuildServiceProvider();
        var readers = new TypedHorseIdentityStoreReaders(builder.Configuration, Options.Create(collectionOptions),
            Options.Create(ReadScheduleOptions(builder)), Options.Create(ReadQueueOptions(builder)), builder.Environment,
            services.GetRequiredService<IEventJsonSerializer>(), services.GetRequiredService<IDomainEventFactory>());
        var engine = new TypedHorseIdentityStoreEngine(readers);
        var fresh = await engine.InspectPreviewAsync(claimed.Manifest, CancellationToken.None).ConfigureAwait(false);
        if (!fresh.CanStage || fresh.Fingerprint is null) return 4;

        var existingSaved = await journal.GetSavedRequestAsync(operationId).ConfigureAwait(false);
        if (existingSaved is not null
            && !string.Equals(existingSaved.PreviewFingerprint, fresh.Fingerprint, StringComparison.Ordinal))
            return 4;
        if (!(await journal.BindStandaloneRequestAsync(operationId, claimed.Manifest, fresh.Fingerprint, now)
                .ConfigureAwait(false)).Succeeded)
            return 4;
        var saved = await journal.GetSavedRequestAsync(operationId).ConfigureAwait(false);
        if (saved is null) return 4;
        var created = await journal.CreateExecutionIntentAsync(operationId, saved.ImmutableRequestHash, now)
            .ConfigureAwait(false);
        if (!created.Succeeded || created.Intent?.Phase != HorseIdentityMigrationExecutionPhase.IntentRecorded)
            return 4;
        if (!(await journal.AdvanceExecutionIntentAsync(operationId, HorseIdentityMigrationExecutionPhase.Preparing,
                now).ConfigureAwait(false)).Succeeded)
            return 4;

        try
        {
            await HorseIdentityMigrationMaintenanceOwnership.InstallStandaloneMaintenanceGatesAsync(
                runtimeOwnership, controlLease, operationId, saved.ImmutableRequestHash, journal,
                CancellationToken.None).ConfigureAwait(false);
            foreach (var phase in new[]
            {
                HorseIdentityMigrationExecutionPhase.Draining,
                HorseIdentityMigrationExecutionPhase.StoppingApi,
                HorseIdentityMigrationExecutionPhase.ApiExited,
            })
                if (!(await journal.AdvanceExecutionIntentAsync(operationId, phase, DateTimeOffset.UtcNow)
                        .ConfigureAwait(false)).Succeeded)
                    return 5;

            var backup = HorseIdentityMigrationManagedBackup.ForJournal(journal);
            await HorseIdentityMigrationSupervisor.CreateOrValidateBackupAsync(backup, operationId, sources, journal,
                CancellationToken.None).ConfigureAwait(false);
            HorseIdentityMigrationSupervisor.EnsureSourceFilesystemCapacity(paths);
            foreach (var phase in new[]
            {
                HorseIdentityMigrationExecutionPhase.BackedUp,
                HorseIdentityMigrationExecutionPhase.Applying,
            })
                if (!(await journal.AdvanceExecutionIntentAsync(operationId, phase, DateTimeOffset.UtcNow)
                        .ConfigureAwait(false)).Succeeded)
                    return 5;

            var authorization = await controlLease.IssueOperationAuthorizationAsync(operationId,
                saved.ImmutableRequestHash, journal).ConfigureAwait(false);
            var result = await engine.ExecuteOwnedAsync(operationId, claimed.Manifest, journal, CancellationToken.None)
                .ConfigureAwait(false);
            if (result.Status != TypedHorseIdentityExecutionStatus.Completed || !result.CanWrite
                || (await journal.GetStatusAsync(operationId).ConfigureAwait(false))?.Status
                    != HorseIdentityMigrationOperationStatus.Succeeded
                || (await journal.GetExecutionIntentAsync(operationId).ConfigureAwait(false))?.Phase
                    != HorseIdentityMigrationExecutionPhase.DataSucceeded)
                return 5;

            if (!(await journal.AdvanceExecutionIntentAsync(operationId,
                    HorseIdentityMigrationExecutionPhase.Verifying, DateTimeOffset.UtcNow).ConfigureAwait(false)).Succeeded)
                return 5;
            await HorseIdentityMigrationSupervisor.VerifyAllDataAsync(operationId, sources, journal,
                backup, CancellationToken.None).ConfigureAwait(false);
            if (!(await journal.AdvanceExecutionIntentAsync(operationId,
                    HorseIdentityMigrationExecutionPhase.Verified, DateTimeOffset.UtcNow).ConfigureAwait(false)).Succeeded)
                return 5;
            await HorseIdentityMigrationMaintenanceOwnership.ClearMaintenanceGatesAsync(runtimeOwnership,
                controlLease, authorization, journal, CancellationToken.None).ConfigureAwait(false);
            return 0;
        }
        catch
        {
            var intent = await journal.GetExecutionIntentAsync(operationId).ConfigureAwait(false);
            var status = await journal.GetStatusAsync(operationId).ConfigureAwait(false);
            if (intent is not null && status?.Status != HorseIdentityMigrationOperationStatus.Succeeded)
                await journal.AdvanceExecutionIntentAsync(operationId, HorseIdentityMigrationExecutionPhase.Failed,
                    DateTimeOffset.UtcNow).ConfigureAwait(false);
            return 5;
        }
    }

    /// <summary>Runs a journal-only host when the journal is not safe for ordinary application startup.</summary>
    internal static async Task<bool> RunBlockedHostIfNeededAsync(WebApplicationBuilder builder,
        bool forceBlocked = false)
    {
        var operatorOnly = IsOperatorOnlyMode();
        var journal = CreateJournal(builder);
        var journalAvailable = true;
        try
        {
            var guard = await journal.InspectStartupGuardAsync(DateTimeOffset.UtcNow).ConfigureAwait(false);
            if (guard.State == HorseIdentityMigrationStartupState.Ready && !forceBlocked && !operatorOnly) return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidDataException or SqliteException)
        {
            journalAvailable = false;
        }

        builder.Services.Configure<ApiKeyOptions>(options =>
        {
            options.HeaderName = builder.Configuration["ApiKey:HeaderName"] ?? "X-Api-Key";
            var configuredKey = builder.Configuration["ApiKey:Key"];
            options.Key = string.IsNullOrWhiteSpace(configuredKey)
                ? Environment.GetEnvironmentVariable("HORSE_RACING_API_KEY")
                : configuredKey;
        });
        builder.Services.AddAdminAuthentication();
        builder.Services.AddAntiforgery();
        ConfigureMinimalOperatorCookieChallenges(builder.Services);
        var app = builder.Build();
        app.UseApiKeyProtection();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.MapGet("/health", () => Results.Ok(new { Status = "ok" }));
        app.MapAdminLoginEndpoints();
        app.MapGet(StatusRoute, async (string operationId, HttpResponse response, CancellationToken cancellationToken) =>
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
                return Results.Ok(HorseIdentityMigrationEndpoints.ToPublic(operation, saved, pending, intent));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidDataException or SqliteException)
            {
                return Results.Json(new HorseIdentityMigrationOperationError("Unavailable"),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
        MapReadonlyVerificationEndpoints(app, journal, allowVerificationActions: !operatorOnly);
        app.MapFallback(async (HttpContext context) =>
        {
            SetNoStore(context.Response);
            if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                || !HttpMethods.IsGet(context.Request.Method)
                || !context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Application startup is blocked by migration state.");

            if (context.User.Identity?.IsAuthenticated != true)
                return Results.Challenge();

            var status = journalAvailable
                ? await ReadOperatorStatusAsync(journal, null, context.RequestAborted)
                    .ConfigureAwait(false)
                : (Operation: (HorseIdentityMigrationOperationResponse?)null, Available: false, CanVerify: false);

            return Results.Content(BuildMinimalOperatorHtml(context.Request.Path, status.Operation, status.Available),
                "text/html; charset=utf-8", System.Text.Encoding.UTF8);
        });
        await app.RunAsync().ConfigureAwait(false);
        return true;
    }

    internal static void ConfigureMinimalOperatorCookieChallenges(IServiceCollection services)
    {
        services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme,
            options =>
            {
                var redirectToLogin = options.Events.OnRedirectToLogin;
                options.Events.OnRedirectToLogin = context =>
                {
                    if (HttpMethods.IsPost(context.Request.Method)
                        && context.Request.Path.StartsWithSegments("/settings/horse-id-migration"))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }
                    return redirectToLogin(context);
                };
            });
    }

    internal static (ServiceProvider Services, TypedHorseIdentityStoreEngine Engine) CreateStoreEngine(
        WebApplicationBuilder builder)
    {
        var services = new ServiceCollection().AddLogging().AddEventFlow(options => options
            .AddDefaults(typeof(HorseAggregate).Assembly)
            .AddDefaults(typeof(RaceAggregate).Assembly)).BuildServiceProvider();
        var readers = new TypedHorseIdentityStoreReaders(builder.Configuration,
            Options.Create(ReadCollectionOptions(builder)), Options.Create(ReadScheduleOptions(builder)),
            Options.Create(ReadQueueOptions(builder)), builder.Environment,
            services.GetRequiredService<IEventJsonSerializer>(), services.GetRequiredService<IDomainEventFactory>());
        return (services, new TypedHorseIdentityStoreEngine(readers));
    }

    private static async Task<(HorseIdentityMigrationOperationResponse? Operation, bool Available, bool CanVerify)> ReadOperatorStatusAsync(
        HorseIdentityMigrationJournal journal, Guid? requestedOperationId, CancellationToken requestAborted)
    {
        using var queryDeadline = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        queryDeadline.CancelAfter(OperatorStatusReadDeadline);
        try
        {
            HorseIdentityMigrationExecutionIntent? activeIntent = null;
            HorseIdentityMigrationJournalOperation? savedOperation;
            if (requestedOperationId is { } requestedId)
            {
                savedOperation = await journal.GetStatusAsync(requestedId, queryDeadline.Token).ConfigureAwait(false);
            }
            else
            {
                activeIntent = await journal.GetActiveExecutionIntentAsync(queryDeadline.Token).ConfigureAwait(false);
                savedOperation = activeIntent is null
                    ? await journal.GetMostRecentOperationAsync(queryDeadline.Token).ConfigureAwait(false)
                    : await journal.GetStatusAsync(activeIntent.OperationId, queryDeadline.Token).ConfigureAwait(false);
            }

            if (savedOperation is null) return (null, true, false);
            var intent = activeIntent is not null && activeIntent.OperationId == savedOperation.OperationId
                ? activeIntent
                : await journal.GetExecutionIntentAsync(savedOperation.OperationId, queryDeadline.Token).ConfigureAwait(false);
            var operation = HorseIdentityMigrationPublicMapper.ToPublic(savedOperation) with
            {
                ExecutionPhase = intent?.Phase.ToString(),
                ExecutionUpdatedAt = intent?.UpdatedAt,
            };
            var canVerify = savedOperation.Status == HorseIdentityMigrationOperationStatus.Succeeded
                && intent is not null
                && await journal.CanBeginReadonlyVerificationAsync(savedOperation.OperationId, queryDeadline.Token)
                    .ConfigureAwait(false);
            return (operation, true, canVerify);
        }
        catch (OperationCanceledException) when (!requestAborted.IsCancellationRequested)
        {
            return (null, false, false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidDataException or SqliteException)
        {
            return (null, false, false);
        }
    }

    private static void SetNoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        response.Headers.Pragma = "no-cache";
    }

    internal static string BuildMinimalOperatorHtml(PathString requestPath,
        HorseIdentityMigrationOperationResponse? operation, bool journalAvailable,
        string verificationActionHtml = "")
    {
        static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
        var pageTitle = operation is null ? "移行状態を確認できません" : "移行状態とエラー";
        var summary = !journalAvailable
            ? "移行状態を確認できません。ジャーナルの内容が読み取れないため、成功とは判断できません。"
            : operation is null
                ? "移行操作の記録がありません。状態は未確認です。"
                : operation.Status switch
                {
                    "Succeeded" => "移行データの成功記録があります。運用状態は画面の最新情報を確認してください。",
                    "Partial" => "移行の一部が完了した可能性があります。確認が終わるまで通常の更新は停止しています。",
                    "Failed" => "移行処理に失敗しました。確認が終わるまで通常の更新は停止しています。",
                    _ => "移行の状態を確認してください。確認が終わるまで通常の更新は停止しています.",
                };
        var status = operation is null ? "Unknown" : E(operation.Status);
        var reason = operation is null ? "Unavailable" : E(operation.Reason);
        var phase = operation?.ExecutionPhase is { Length: > 0 } value ? E(value) : "Unknown";
        var updatedAt = operation is null ? "Unknown" : E(operation.UpdatedAt.ToString("O"));
        var operationId = operation is null ? "Unknown" : E(operation.OperationId.ToString("D"));
        var detailsHref = operation is null
            ? null
            : $"/settings/horse-id-migration?operationId={operation.OperationId:D}";
        var detailsLink = detailsHref is null
            ? string.Empty
            : $"<p><a href=\"{E(detailsHref)}\">移行状態の詳細</a></p>";
        static bool IsFailure(string code) => code is "PreparationFailed" or "BackupFailed" or "InspectionFailed"
            or "ApplyFailed" or "VerificationFailed" or "ChildFailed" or "StartupFailed";
        var failureResults = operation?.Results.Where(item => IsFailure(item.Code)).TakeLast(7).ToArray() ?? [];
        var results = operation is null || operation.Results.Count == 0
            ? "<li>追加の結果情報はありません。</li>"
            : string.Join("", failureResults.Concat(operation.Results.Where(item => !IsFailure(item.Code))
                    .Take(32 - failureResults.Length))
                .Select(item => $"<li>{E(item.Code)}</li>"));
        var refreshPath = requestPath.HasValue ? E(requestPath.Value) : "/";
        return $$$"""
            <!doctype html>
            <html lang="ja">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>{{{E(pageTitle)}}}</title>
            <style>body{font-family:system-ui,sans-serif;line-height:1.6;margin:0;background:#f5f6f8;color:#20242a}main{max-width:48rem;margin:2rem auto;padding:1.25rem;background:#fff;border:1px solid #d5d9df;border-radius:.5rem}h1{font-size:1.5rem}dl{display:grid;grid-template-columns:minmax(8rem,auto) 1fr;gap:.5rem 1rem}dt{font-weight:700}dd{margin:0;overflow-wrap:anywhere}button{font:inherit;padding:.55rem .9rem}a{color:#0757a6}@media(max-width:40rem){main{margin:.75rem;padding:1rem}dl{grid-template-columns:1fr;gap:0}dd{margin:0 0 .6rem}}</style></head>
            <body><main><h1>{{{E(pageTitle)}}}</h1><p role="status">{{{E(summary)}}}</p>
            <p>Status: <strong>{{{status}}}</strong></p>
            <dl><dt>状態</dt><dd>{{{status}}}</dd><dt>理由</dt><dd>{{{reason}}}</dd><dt>確認できた段階</dt><dd>{{{phase}}}</dd>
            <dt>記録更新時刻</dt><dd>{{{updatedAt}}}</dd><dt>操作番号</dt><dd>{{{operationId}}}</dd></dl>
            <h2>結果</h2><ul>{{{results}}}</ul>{{{verificationActionHtml}}}<form method="get" action="{{{refreshPath}}}"><button type="submit">状態を更新</button></form>
            {{{detailsLink}}}</main></body></html>
            """;
    }

    internal static void MapReadonlyVerificationEndpoints(IEndpointRouteBuilder endpoints,
        HorseIdentityMigrationJournal journal, bool allowVerificationActions = true)
    {
        if (allowVerificationActions)
        {
            endpoints.MapPost(StatusRoute + "/verify", async (
                string operationId, HttpContext context, IAntiforgery antiforgery,
                CancellationToken cancellationToken) =>
            {
                SetNoStore(context.Response);
                if (!Guid.TryParseExact(operationId, "D", out var parsed) || parsed == Guid.Empty)
                    return Results.NotFound();
                if (!await antiforgery.IsRequestValidAsync(context).ConfigureAwait(false))
                    return Results.BadRequest(new HorseIdentityMigrationOperationError("InvalidCsrf"));
                try
                {
                    var outcome = await BeginReadonlyVerificationAsync(parsed, context, journal, cancellationToken)
                        .ConfigureAwait(false);
                    return outcome is null ? Results.Conflict(new HorseIdentityMigrationOperationError("NotEligible"))
                        : Results.Accepted($"/api/v2/admin/horse-id-migration-operations/{parsed:D}", outcome);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                    or InvalidDataException or SqliteException)
                {
                    return Results.Json(new HorseIdentityMigrationOperationError("Unavailable"),
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            }).RequireAuthorization();
        }

        endpoints.MapGet("/settings/horse-id-migration", async (
            string? operationId, HttpContext context, IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context.Response);
            Guid? requestedOperationId = null;
            if (operationId is not null)
            {
                if (!Guid.TryParseExact(operationId, "D", out var parsed) || parsed == Guid.Empty)
                    return Results.NotFound();
                requestedOperationId = parsed;
            }
            var status = await ReadOperatorStatusAsync(journal, requestedOperationId, cancellationToken)
                .ConfigureAwait(false);
            if (requestedOperationId is not null && status.Available && status.Operation is null)
                return Results.NotFound();
            var verificationAction = string.Empty;
            if (allowVerificationActions && status.CanVerify)
            {
                var token = antiforgery.GetAndStoreTokens(context).RequestToken;
                var tokenHtml = WebUtility.HtmlEncode(token);
                var operationIdHtml = WebUtility.HtmlEncode(status.Operation!.OperationId.ToString("D"));
                verificationAction = $"<form method=\"post\" action=\"/settings/horse-id-migration/{operationIdHtml}/verify\"><input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{tokenHtml}\"><button type=\"submit\">Run read-only verification</button></form>";
            }
            return Results.Content(BuildMinimalOperatorHtml(context.Request.Path, status.Operation, status.Available,
                    verificationAction),
                "text/html; charset=utf-8", System.Text.Encoding.UTF8,
                status.Available ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        }).RequireAuthorization();

        if (allowVerificationActions)
        {
            endpoints.MapPost("/settings/horse-id-migration/{operationId:guid}/verify", async (
                Guid operationId, HttpContext context, IAntiforgery antiforgery,
                CancellationToken cancellationToken) =>
            {
                SetNoStore(context.Response);
                if (operationId == Guid.Empty || !await antiforgery.IsRequestValidAsync(context).ConfigureAwait(false))
                    return Results.BadRequest("Verification request was not accepted.");
                try
                {
                    var outcome = await BeginReadonlyVerificationAsync(operationId, context, journal, cancellationToken)
                        .ConfigureAwait(false);
                    var statusCode = outcome is null ? StatusCodes.Status409Conflict : StatusCodes.Status202Accepted;
                    var message = outcome is null ? "This operation is not eligible for read-only verification."
                        : "Read-only verification was requested. The supervisor will keep the maintenance gate until verification completes.";
                    return Results.Content($"<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><meta name=\"robots\" content=\"noindex\"><title>Horse identity migration</title><main><h1>Horse identity migration</h1><p>{WebUtility.HtmlEncode(message)}</p><p>Operation: <code>{WebUtility.HtmlEncode(operationId.ToString("D"))}</code></p></main></html>",
                        "text/html; charset=utf-8", statusCode: statusCode);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                    or InvalidDataException or SqliteException)
                {
                    return Results.Content("<!doctype html><html><meta charset=\"utf-8\"><title>Status unavailable</title><p>Migration status is unavailable.</p></html>",
                        "text/html; charset=utf-8", statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            }).RequireAuthorization();
        }
    }

    private static async Task<HorseIdentityMigrationOperationResponse?> BeginReadonlyVerificationAsync(
        Guid operationId, HttpContext context, HorseIdentityMigrationJournal journal,
        CancellationToken cancellationToken)
    {
        var operation = await journal.GetStatusAsync(operationId, cancellationToken).ConfigureAwait(false);
        var intent = await journal.GetExecutionIntentAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (operation?.Status != HorseIdentityMigrationOperationStatus.Succeeded || intent is null
            || !await journal.CanBeginReadonlyVerificationAsync(operationId, cancellationToken).ConfigureAwait(false))
            return null;
        if (intent.Phase == HorseIdentityMigrationExecutionPhase.DataSucceeded)
        {
            var advanced = await journal.AdvanceExecutionIntentAsync(operationId,
                HorseIdentityMigrationExecutionPhase.Verifying, DateTimeOffset.UtcNow, cancellationToken)
                .ConfigureAwait(false);
            if (!advanced.Succeeded) return null;
            intent = advanced.Intent;
        }

        Environment.ExitCode = HorseIdentityMigrationSupervisor.VerifyReadyExitCode;
        context.RequestServices.GetRequiredService<IHostApplicationLifetime>().StopApplication();
        var saved = await journal.GetSavedRequestAsync(operationId, CancellationToken.None).ConfigureAwait(false);
        var pending = saved is null
            ? await journal.GetPendingRequestAsync(operationId, CancellationToken.None).ConfigureAwait(false)
            : null;
        return HorseIdentityMigrationEndpoints.ToPublic(operation, saved, pending, intent);
    }

    internal static HorseIdentityMigrationJournal CreateJournal(WebApplicationBuilder builder)
    {
        var collection = ReadCollectionOptions(builder);
        var paths = ResolveConfiguredSources(builder, collection);
        return new(collection.StateDirectory, paths.PhysicalSourcePaths);
    }

    internal static HorseIdentityMigrationRuntimeOwnership AcquireConfiguredSourceOwnership(
        WebApplicationBuilder builder)
    {
        var paths = ResolveConfiguredSources(builder, ReadCollectionOptions(builder));
        return HorseIdentityMigrationRuntimeOwnership.Acquire(paths.PhysicalSourcePaths);
    }

    internal static HorseIdentityMigrationConfiguredSources ResolveConfiguredSources(
        WebApplicationBuilder builder, CollectionPlatformOptions collection) =>
        HorseIdentityMigrationConfiguredSources.Resolve(builder.Configuration, collection,
            ReadScheduleOptions(builder), ReadQueueOptions(builder), builder.Environment.ContentRootPath,
            Environment.CurrentDirectory, AppContext.BaseDirectory);

    internal static CollectionPlatformOptions ReadCollectionOptions(WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(CollectionPlatformOptions.SectionName)
            .Get<CollectionPlatformOptions>() ?? new();
        var directory = string.IsNullOrWhiteSpace(options.StateDirectory)
            ? "collection-platform-state" : options.StateDirectory;
        options.StateDirectory = Path.IsPathRooted(directory)
            ? Path.GetFullPath(directory)
            : Path.GetFullPath(directory, builder.Environment.ContentRootPath);
        return options;
    }

    private static PredictionScheduleOptions ReadScheduleOptions(WebApplicationBuilder builder) =>
        builder.Configuration.GetSection(PredictionScheduleOptions.SectionName)
            .Get<PredictionScheduleOptions>() ?? new();

    private static CollectionQueueOptions ReadQueueOptions(WebApplicationBuilder builder) =>
        builder.Configuration.GetSection(CollectionQueueOptions.SectionName)
            .Get<CollectionQueueOptions>() ?? new();
}
