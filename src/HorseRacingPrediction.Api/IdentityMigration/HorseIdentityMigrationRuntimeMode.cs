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
        var journal = CreateJournal(builder);
        var guard = await journal.InspectStartupGuardAsync(DateTimeOffset.UtcNow).ConfigureAwait(false);
        if (guard.State == HorseIdentityMigrationStartupState.Ready && !forceBlocked) return false;

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
        MapReadonlyVerificationEndpoints(app, journal);
        app.MapFallback(() => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Application startup is blocked by migration state."));
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

    private static void SetNoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        response.Headers.Pragma = "no-cache";
    }

    internal static void MapReadonlyVerificationEndpoints(IEndpointRouteBuilder endpoints,
        HorseIdentityMigrationJournal journal)
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

        endpoints.MapGet("/settings/horse-id-migration", async (
            string? operationId, HttpContext context, IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context.Response);
            if (!Guid.TryParseExact(operationId, "D", out var parsed) || parsed == Guid.Empty)
                return Results.NotFound();
            try
            {
                var operation = await journal.GetStatusAsync(parsed, cancellationToken).ConfigureAwait(false);
                if (operation is null) return Results.NotFound();
                var safeStatus = HorseIdentityMigrationPublicMapper.Status(operation.Status);
                var idHtml = WebUtility.HtmlEncode(operation.OperationId.ToString("D"));
                var statusHtml = WebUtility.HtmlEncode(safeStatus);
                var intent = await journal.GetExecutionIntentAsync(parsed, cancellationToken).ConfigureAwait(false);
                var canVerify = operation.Status == HorseIdentityMigrationOperationStatus.Succeeded && intent is not null
                    && await journal.CanBeginReadonlyVerificationAsync(parsed, cancellationToken).ConfigureAwait(false);
                var action = string.Empty;
                if (canVerify)
                {
                    var token = antiforgery.GetAndStoreTokens(context).RequestToken;
                    var tokenHtml = WebUtility.HtmlEncode(token);
                    action = $"<form method=\"post\" action=\"/settings/horse-id-migration/{idHtml}/verify\"><input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{tokenHtml}\"><button type=\"submit\">Run read-only verification</button></form>";
                }
                return Results.Content($"<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><meta name=\"robots\" content=\"noindex\"><title>Horse identity migration status</title><main><h1>Horse identity migration</h1><p>Operation: <code>{idHtml}</code></p><p>Status: <strong>{statusHtml}</strong></p>{action}</main></html>",
                    "text/html; charset=utf-8");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidDataException or SqliteException)
            {
                return Results.Content("<!doctype html><html><meta charset=\"utf-8\"><title>Status unavailable</title><p>Migration status is unavailable.</p></html>",
                    "text/html; charset=utf-8", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireAuthorization();

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
