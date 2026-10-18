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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using HorseRacingPrediction.Api.Web;
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
            var guard = await journal.InspectStartupGuardAsync(DateTimeOffset.UtcNow).ConfigureAwait(false);
            if (guard.State != HorseIdentityMigrationStartupState.Ready) return 3;

            var operationId = selection.OperationId!.Value;
            var prior = await journal.GetStatusAsync(operationId).ConfigureAwait(false);
            if (prior?.Status == HorseIdentityMigrationOperationStatus.Succeeded) return 0;
            if (prior is not null && prior.Status != HorseIdentityMigrationOperationStatus.Staged) return 4;

            using var sourceOwnership = AcquireConfiguredSourceOwnership(builder);

            var claim = await journal.ClaimWorkflowAsync(operationId, DateTimeOffset.UtcNow).ConfigureAwait(false);
            if (!claim.Succeeded) return 4;

            var operation = await journal.GetClaimedStagedRequestAsync(operationId).ConfigureAwait(false);
            if (operation is null) return 4;

            var collection = ReadCollectionOptions(builder);
            var schedule = ReadScheduleOptions(builder);
            var queue = ReadQueueOptions(builder);
            var services = new ServiceCollection().AddLogging().AddEventFlow(options => options
                .AddDefaults(typeof(HorseAggregate).Assembly)
                .AddDefaults(typeof(RaceAggregate).Assembly)).BuildServiceProvider();
            using (services)
            {
                var readers = new TypedHorseIdentityStoreReaders(builder.Configuration,
                    Options.Create(collection), Options.Create(schedule), Options.Create(queue), builder.Environment,
                    services.GetRequiredService<IEventJsonSerializer>(), services.GetRequiredService<IDomainEventFactory>());
                var engine = new TypedHorseIdentityStoreEngine(readers);
                var result = await engine.ExecuteOwnedAsync(operation.OperationId, operation.Manifest, journal,
                    CancellationToken.None).ConfigureAwait(false);
                return result.Status == TypedHorseIdentityExecutionStatus.Completed && result.CanWrite ? 0 : 5;
            }
        }
        catch (OperationCanceledException) { return 5; }
        catch (Exception) { return 5; }
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
        var app = builder.Build();
        app.UseApiKeyProtection();
        app.UseAuthentication();
        app.UseAuthorization();
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
                return operation is null
                    ? Results.NotFound()
                    : Results.Ok(HorseIdentityMigrationPublicMapper.ToPublic(operation));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidDataException or SqliteException)
            {
                return Results.Json(new HorseIdentityMigrationOperationError("Unavailable"),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
        app.MapGet("/settings/horse-id-migration", async (
            string? operationId, HttpResponse response, CancellationToken cancellationToken) =>
        {
            SetNoStore(response);
            if (!Guid.TryParseExact(operationId, "D", out var parsed) || parsed == Guid.Empty)
                return Results.NotFound();
            try
            {
                var operation = await journal.GetStatusAsync(parsed, cancellationToken).ConfigureAwait(false);
                if (operation is null) return Results.NotFound();
                var safeStatus = HorseIdentityMigrationPublicMapper.Status(operation.Status);
                var idHtml = WebUtility.HtmlEncode(operation.OperationId.ToString("D"));
                var statusHtml = WebUtility.HtmlEncode(safeStatus);
                return Results.Content($"<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><meta name=\"robots\" content=\"noindex\"><title>Horse identity migration status</title><main><h1>Horse identity migration</h1><p>Operation: <code>{idHtml}</code></p><p>Status: <strong>{statusHtml}</strong></p></main></html>",
                    "text/html; charset=utf-8");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidDataException or SqliteException)
            {
                return Results.Content("<!doctype html><html><meta charset=\"utf-8\"><title>Status unavailable</title><p>Migration status is unavailable.</p></html>",
                    "text/html; charset=utf-8", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireAuthorization();
        app.MapFallback(() => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Application startup is blocked by migration state."));
        await app.RunAsync().ConfigureAwait(false);
        return true;
    }

    private static void SetNoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        response.Headers.Pragma = "no-cache";
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

    private static HorseIdentityMigrationConfiguredSources ResolveConfiguredSources(
        WebApplicationBuilder builder, CollectionPlatformOptions collection) =>
        HorseIdentityMigrationConfiguredSources.Resolve(builder.Configuration, collection,
            ReadScheduleOptions(builder), ReadQueueOptions(builder), builder.Environment.ContentRootPath,
            Environment.CurrentDirectory, AppContext.BaseDirectory);

    private static CollectionPlatformOptions ReadCollectionOptions(WebApplicationBuilder builder)
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
