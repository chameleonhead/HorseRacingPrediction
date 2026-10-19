using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using EventFlow;
using EventFlow.EntityFramework;
using EventFlow.EntityFramework.Extensions;
using EventFlow.Extensions;
using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.Api.IdentityMigration;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Application.Commands.Predictions;
using HorseRacingPrediction.Contracts.Identity;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web;
using HorseRacingPrediction.Api.Web.ApiBrowsing;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Domain.Predictions;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityMigrationRuntimeModeTests
{
    [TestMethod]
    public async Task FixedCommandRejectsMissingMalformedAndExtraArgumentsBeforeCreatingHostState()
    {
        var missing = HorseIdentityMigrationRuntimeMode.Parse([HorseIdentityMigrationRuntimeMode.CommandName]);
        var malformed = HorseIdentityMigrationRuntimeMode.Parse([HorseIdentityMigrationRuntimeMode.CommandName, "not-a-guid"]);
        var extra = HorseIdentityMigrationRuntimeMode.Parse([HorseIdentityMigrationRuntimeMode.CommandName,
            Guid.NewGuid().ToString("D"), "--urls", "http://127.0.0.1:0"]);

        Assert.AreEqual(HorseIdentityMigrationRuntimeModeKind.InvalidCommand, missing.Kind);
        Assert.AreEqual(HorseIdentityMigrationRuntimeModeKind.InvalidCommand, malformed.Kind);
        Assert.AreEqual(HorseIdentityMigrationRuntimeModeKind.InvalidCommand, extra.Kind);
        Assert.AreEqual(2, await HorseIdentityMigrationRuntimeMode.RunFixedCommandAsync(
            [HorseIdentityMigrationRuntimeMode.CommandName, "not-a-guid"]));
        Assert.AreEqual(HorseIdentityMigrationRuntimeModeKind.NormalHost,
            HorseIdentityMigrationRuntimeMode.Parse(["--urls", "http://127.0.0.1:5010"]).Kind);
        Assert.AreEqual(HorseIdentityMigrationRuntimeModeKind.ExecuteOperation,
            HorseIdentityMigrationRuntimeMode.Parse([HorseIdentityMigrationRuntimeMode.CommandName,
                Guid.NewGuid().ToString("D")]).Kind);
    }

    [TestMethod]
    public async Task ChildHelperTimesOutAndKillsTheNormalHostProcessTree()
    {
        await using var stores = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();

        var result = await RunMigrationCommandAsync(stores,
            ["--urls", "http://127.0.0.1:0"], TimeSpan.FromSeconds(5), configureIsolatedAws: true);

        Assert.IsTrue(result.TimedOut,
            $"The normal host should remain alive until the bounded child wait expires. Child output: {result.Output}");
        Assert.IsTrue(result.ExitedAfterCleanup,
            $"The child process should be reaped by the timeout cleanup. Child output: {result.Output}");
        try
        {
            using var child = Process.GetProcessById(result.ProcessId);
            Assert.IsTrue(child.HasExited, "The child process must not remain alive after timeout cleanup.");
        }
        catch (ArgumentException)
        {
            // The OS has already removed the terminated child process.
        }
    }

    [TestMethod]
    public async Task BlockedHostServesHealthAndAuthenticatedJournalStatusWithoutCreatingBusinessStores()
    {
        using var fixture = new RuntimeFixture();
        var operationId = Guid.NewGuid();
        Assert.IsTrue((await fixture.Journal.StageAsync(operationId, Manifest(), DateTimeOffset.UtcNow)).Succeeded);
        Assert.IsTrue((await fixture.Journal.ClaimWorkflowAsync(operationId, DateTimeOffset.UtcNow)).Succeeded);
        await using (var lease = await fixture.Journal.TryAcquireExecutionAsync(operationId, DateTimeOffset.UtcNow)
                     ?? throw new InvalidOperationException("The test operation did not acquire ownership."))
            await lease.MarkPartialAsync(DateTimeOffset.UtcNow);

        using var child = fixture.StartApiProcess();
        try
        {
            using var client = await fixture.WaitForHealthAsync(child);
            using var health = await client.GetAsync("/health");
            Assert.AreEqual(HttpStatusCode.OK, health.StatusCode);

            using var loginPage = await client.GetAsync($"/settings/horse-id-migration?operationId={operationId:D}");
            Assert.AreEqual(HttpStatusCode.OK, loginPage.StatusCode,
                "The cookie-protected journal status page should redirect anonymous users to the existing login page.");
            StringAssert.Contains(await loginPage.Content.ReadAsStringAsync(), "ログイン");

            using var login = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = "user",
                ["password"] = fixture.ApiKey,
                ["ReturnUrl"] = $"/settings/horse-id-migration?operationId={operationId:D}",
            }));
            Assert.AreEqual(HttpStatusCode.OK, login.StatusCode,
                "Successful cookie login should reach the journal-only status page after its redirect.");
            var html = await login.Content.ReadAsStringAsync();
            StringAssert.Contains(html, "Status: <strong>Partial</strong>");
            StringAssert.Contains(html, operationId.ToString("D"));
            Assert.AreEqual("no-store, no-cache, max-age=0", login.Headers.CacheControl?.ToString());

            using var denied = await client.GetAsync($"/api/v2/admin/horse-id-migration-operations/{operationId:D}");
            Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode,
                "A valid cookie must not bypass API-key protection for JSON status.");

            client.DefaultRequestHeaders.Add("X-Test-Horse-Key", fixture.ApiKey);
            using var status = await client.GetAsync($"/api/v2/admin/horse-id-migration-operations/{operationId:D}");
            Assert.AreEqual(HttpStatusCode.OK, status.StatusCode);
            var body = await status.Content.ReadAsStringAsync();
            StringAssert.Contains(body, "\"status\":\"Partial\"");
            Assert.AreEqual("no-store, no-cache, max-age=0", status.Headers.CacheControl?.ToString());
            using var diagnostics = await client.GetAsync("/api/v2/admin/horse-id-migration-schema-diagnostics");
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, diagnostics.StatusCode,
                "The blocked host must not resolve or read configured source stores.");
            Assert.IsFalse(File.Exists(fixture.EventStorePath));
            Assert.IsFalse(File.Exists(fixture.CollectionStorePath));
            Assert.IsFalse(File.Exists(fixture.ScheduleStorePath));
            Assert.IsFalse(File.Exists(fixture.QueueStorePath));
        }
        finally
        {
            await RuntimeFixture.StopAsync(child);
        }
    }

    [TestMethod]
    public async Task LiveOwnerIsNotInterruptedByBlockedHostStartup()
    {
        using var fixture = new RuntimeFixture();
        var operationId = Guid.NewGuid();
        Assert.IsTrue((await fixture.Journal.StageAsync(operationId, Manifest(), DateTimeOffset.UtcNow)).Succeeded);
        Assert.IsTrue((await fixture.Journal.ClaimWorkflowAsync(operationId, DateTimeOffset.UtcNow)).Succeeded);
        await using var lease = await fixture.Journal.TryAcquireExecutionAsync(operationId, DateTimeOffset.UtcNow)
            ?? throw new InvalidOperationException("The test operation did not acquire ownership.");

        using var child = fixture.StartApiProcess();
        try
        {
            using var client = await fixture.WaitForHealthAsync(child);
            client.DefaultRequestHeaders.Add("X-Test-Horse-Key", fixture.ApiKey);
            using var status = await client.GetAsync($"/api/v2/admin/horse-id-migration-operations/{operationId:D}");
            Assert.AreEqual(HttpStatusCode.OK, status.StatusCode);
            Assert.AreEqual(HorseIdentityMigrationOperationStatus.Running,
                (await fixture.Journal.GetStatusAsync(operationId))!.Status);
        }
        finally
        {
            await RuntimeFixture.StopAsync(child);
        }
    }

    [TestMethod]
    public async Task MinimalHostReadonlyVerificationRequiresCookieAndAntiforgeryAndTouchesOnlyJournal()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hrp-readonly-ui-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var state = Path.Combine(root, "journal");
        var sourcePaths = new[] { Path.Combine(root, "event.db"), Path.Combine(root, "collection.db") };
        var journal = new HorseIdentityMigrationJournal(state, sourcePaths);
        var operationId = Guid.NewGuid();
        var manifest = Manifest();
        var fingerprint = new string('A', 64);
        var requestHash = HorseIdentityMigrationExecutionPolicy.ComputeImmutableRequestHash(manifest, fingerprint);
        Assert.IsTrue((await journal.StageWithRequestKeyAsync(operationId, manifest, Guid.NewGuid(), requestHash,
            fingerprint, DateTimeOffset.UtcNow)).Succeeded);
        Assert.IsTrue((await journal.CreateExecutionIntentAsync(operationId, requestHash, DateTimeOffset.UtcNow)).Succeeded);
        var now = DateTimeOffset.UtcNow;
        foreach (var phase in new[]
        {
            HorseIdentityMigrationExecutionPhase.Preparing,
            HorseIdentityMigrationExecutionPhase.Draining,
            HorseIdentityMigrationExecutionPhase.StoppingApi,
            HorseIdentityMigrationExecutionPhase.ApiExited,
            HorseIdentityMigrationExecutionPhase.BackedUp,
            HorseIdentityMigrationExecutionPhase.Applying,
        })
            Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId, phase, now)).Succeeded);
        Assert.IsTrue((await journal.ClaimWorkflowAsync(operationId, now)).Succeeded);
        await using (var lease = await journal.TryAcquireExecutionAsync(operationId, now)
            ?? throw new AssertFailedException("The readonly verification fixture must acquire its journal execution."))
        {
            var evidence = new TypedHorseIdentityPageEvidence(MigrationStoreSurface.EventStore, "EventEntity", 1,
                new string('B', 64), new string('C', 64), "0", "0");
            var prepared = new HorseIdentityMigrationPageCheckpoint(evidence, new string('D', 64),
                HorseIdentityMigrationPageState.Prepared);
            Assert.IsTrue((await journal.RegisterRequiredPagesAsync(operationId, [prepared], now)).Succeeded);
            foreach (var pageState in new[]
            {
                HorseIdentityMigrationPageState.Prepared,
                HorseIdentityMigrationPageState.CommittedUnverified,
                HorseIdentityMigrationPageState.Verified,
            })
                Assert.IsTrue((await journal.RecordPageCheckpointAsync(operationId,
                    prepared with { State = pageState }, now)).Succeeded);
            await lease.CompleteAsync(now);
        }
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.Succeeded,
            (await journal.GetStatusAsync(operationId))!.Status);
        Assert.AreEqual(HorseIdentityMigrationExecutionPhase.DataSucceeded,
            (await journal.GetExecutionIntentAsync(operationId))!.Phase);
        Assert.IsTrue(await journal.CanBeginReadonlyVerificationAsync(operationId));

        var previousExitCode = Environment.ExitCode;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.Configure<ApiKeyOptions>(options => options.Key = "test-only-key");
        builder.Services.AddAdminAuthentication();
        HorseIdentityMigrationRuntimeMode.ConfigureMinimalOperatorCookieChallenges(builder.Services);
        builder.Services.AddAntiforgery();
        var app = builder.Build();
        var hostLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        app.UseApiKeyProtection();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.MapAdminLoginEndpoints();
        HorseIdentityMigrationRuntimeMode.MapReadonlyVerificationEndpoints(app, journal);
        await app.StartAsync();
        try
        {
            using var client = app.GetTestClient();
            using var anonymous = await client.GetAsync($"/settings/horse-id-migration?operationId={operationId:D}");
            Assert.AreEqual(HttpStatusCode.Redirect, anonymous.StatusCode);
            Assert.IsTrue(anonymous.Headers.Location?.ToString().Contains("/login", StringComparison.Ordinal));
            Assert.AreEqual(HttpStatusCode.Unauthorized,
                (await client.PostAsync($"/settings/horse-id-migration/{operationId:D}/verify",
                    new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode);

            using var login = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = "user",
                ["password"] = "test-only-key",
                ["ReturnUrl"] = $"/settings/horse-id-migration?operationId={operationId:D}",
            }));
            Assert.AreEqual(HttpStatusCode.Redirect, login.StatusCode);
            var authCookie = login.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
            using var statusRequest = new HttpRequestMessage(HttpMethod.Get,
                $"/settings/horse-id-migration?operationId={operationId:D}");
            statusRequest.Headers.Add("Cookie", authCookie);
            using var status = await client.SendAsync(statusRequest);
            Assert.AreEqual(HttpStatusCode.OK, status.StatusCode);
            var html = await status.Content.ReadAsStringAsync();
            StringAssert.Contains(html, "Run read-only verification");
            var tokenMatch = System.Text.RegularExpressions.Regex.Match(html,
                "name=\\\"__RequestVerificationToken\\\" value=\\\"([^\\\"]+)\\\"");
            Assert.IsTrue(tokenMatch.Success, "The status page must include an antiforgery token only for the eligible operation.");
            var antiforgeryCookie = status.Headers.GetValues("Set-Cookie").Single(value =>
                !value.StartsWith("HorseRacingPrediction.Admin=", StringComparison.Ordinal)).Split(';')[0];

            using var rejected = new HttpRequestMessage(HttpMethod.Post,
                $"/settings/horse-id-migration/{operationId:D}/verify")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>()),
            };
            rejected.Headers.Add("Cookie", authCookie + "; " + antiforgeryCookie);
            Assert.AreEqual(HttpStatusCode.BadRequest, (await client.SendAsync(rejected)).StatusCode);
            Assert.AreEqual(HorseIdentityMigrationExecutionPhase.DataSucceeded,
                (await journal.GetExecutionIntentAsync(operationId))!.Phase);

            using var accepted = new HttpRequestMessage(HttpMethod.Post,
                $"/settings/horse-id-migration/{operationId:D}/verify")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = tokenMatch.Groups[1].Value,
                }),
            };
            accepted.Headers.Add("Cookie", authCookie + "; " + antiforgeryCookie);
            using var response = await client.SendAsync(accepted);
            Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
            StringAssert.Contains((await response.Content.ReadAsStringAsync()).ToLowerInvariant(),
                "read-only verification was requested");
            Assert.IsTrue(hostLifetime.ApplicationStopping.IsCancellationRequested);
            Assert.AreEqual(HorseIdentityMigrationExecutionPhase.Verifying,
                (await journal.GetExecutionIntentAsync(operationId))!.Phase);
            Assert.AreEqual(HorseIdentityMigrationSupervisor.VerifyReadyExitCode, Environment.ExitCode);
            Assert.IsTrue(sourcePaths.All(path => !File.Exists(path)),
                "The minimal operator surface must not create or inspect business stores.");
        }
        finally
        {
            Environment.ExitCode = previousExitCode;
            await app.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task NormalHostRefusesSourceOwnershipWithoutBootstrappingConfiguredStores()
    {
        using var fixture = new RuntimeFixture();
        using var heldOwnership = HorseIdentityMigrationRuntimeOwnership.Acquire(
            [fixture.EventStorePath, fixture.CollectionStorePath, fixture.ScheduleStorePath, fixture.QueueStorePath]);

        using var child = fixture.StartApiProcess();
        try
        {
            using var client = await fixture.WaitForHealthAsync(child);
            client.DefaultRequestHeaders.Add("X-Test-Horse-Key", fixture.ApiKey);
            using var response = await client.GetAsync("/api/v2/admin/horse-id-migration-schema-diagnostics");
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode,
                "A source owner must prevent the ordinary API bootstrap and expose only the journal-only host.");
            Assert.IsFalse(File.Exists(fixture.EventStorePath));
            Assert.IsFalse(File.Exists(fixture.CollectionStorePath));
            Assert.IsFalse(File.Exists(fixture.ScheduleStorePath));
            Assert.IsFalse(File.Exists(fixture.QueueStorePath));
        }
        finally
        {
            await RuntimeFixture.StopAsync(child);
        }
    }

    [TestMethod]
    public async Task InProcessHostShutdownDisposesItsSourceOwnership()
    {
        var root = Path.Combine(Path.GetTempPath(), $"horse-source-host-disposal-{Guid.NewGuid():N}");
        var sourcePath = Path.Combine(root, "source.db");
        var ownership = HorseIdentityMigrationRuntimeOwnership.Acquire([sourcePath]);
        using (ownership)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Development",
                ContentRootPath = root,
            });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(ownership);
            var app = builder.Build();
            try
            {
                await app.StartAsync();
                await app.StopAsync();
            }
            finally
            {
                await app.DisposeAsync();
            }
        }

        using var reacquired = HorseIdentityMigrationRuntimeOwnership.Acquire([sourcePath]);
        Assert.IsFalse(File.Exists(sourcePath));
    }

    [TestMethod]
    public async Task NormalHostRegistersRealPreviewAndJournalServicesBehindTheAdminApiClient()
    {
        using var fixture = new RuntimeFixture();
        using var child = fixture.StartApiProcess(maintenanceMode: true);
        try
        {
            using var transport = await fixture.WaitForHealthAsync(child);
            var client = new AdminApiClient(transport, new AdminApiBaseAddressResolver(new StubServer()),
                Options.Create(new ApiKeyOptions { HeaderName = "X-Test-Horse-Key", Key = fixture.ApiKey }));
            var preview = await client.PreviewHorseIdentityMigrationAsync(Manifest());
            Assert.IsNotNull(preview,
                "The normal Program registrations must resolve the configured reader, EventFlow codecs, and engine.");
            Assert.IsFalse(preview.CanStage, "An empty configured source has no canonical horse root to admit.");
            var repeatedPreview = await client.PreviewHorseIdentityMigrationAsync(Manifest());
            Assert.IsNotNull(repeatedPreview);
            Assert.AreEqual(preview.Fingerprint, repeatedPreview.Fingerprint,
                "Repeated requests through the real host must read stable evidence without changing source state.");
            Assert.IsNull(await client.GetHorseIdentityMigrationOperationAsync(Guid.NewGuid()),
                "The actual Program journal registration should serve a missing status as not found.");
        }
        finally
        {
            await RuntimeFixture.StopAsync(child);
        }
    }

    [TestMethod]
    public async Task FixedCommandRunsClaimedOperationAndSuccessfulGuidRetryReturnsReceiptWithoutRewriting()
    {
        await using var stores = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        const string oldHorseId = "horse-11111111-1111-4111-8111-111111111111";
        const string raceId = "202609190101";
        const string ticketId = "predictionticket-00000000-0000-4000-8000-000000000201";
        await stores.SeedHorseAsync(oldHorseId);
        await stores.SeedSerializedHorseEventAsync(oldHorseId);
        await stores.SeedJraResourceAsync(oldHorseId);
        var oldEntryId = DeterministicIdGenerator.BuildRaceEntryId(raceId, oldHorseId);
        await stores.SeedInterleavedTicketStreamAcrossPageBoundaryAsync(oldEntryId);
        await stores.ExecuteAsync(stores.CollectionPath, $$"""
            UPDATE collection_resources
            SET AttributesJson='{"requestedByHorseId":"{{oldHorseId}}","userNote":"{{oldHorseId}}"}'
            WHERE Type='Horse' AND Provider='JRA' AND ResourceId='{{oldHorseId}}';
            """);
        await stores.ExecuteAsync(stores.SchedulePath, """
            INSERT INTO prediction_candidates
                (RaceId, Status, FirstQueuedAt, AvailableAt, LeaseToken, LeaseExpiresAt, LastError, UpdatedAt)
            VALUES ('preserve-schedule-race', 0, '2026-10-14T00:00:00+09:00',
                    '2026-10-14T00:00:00+09:00', NULL, NULL, 'preserve error', '2026-10-14T00:00:00+09:00');
            """);
        await stores.ExecuteAsync(stores.EventStorePath, $$"""
            INSERT INTO JraSubjectProfileReadModel
                (rowid, SubjectId, Name, SourceIdentity, SourceUrl, AcquiredAt, Fields)
            VALUES (0, '{{oldHorseId}}', 'preserve name', 'preserve source', 'https://example.invalid/profile',
                    '2026-10-14T00:00:00+09:00', '{"display":"preserve"}');
            INSERT INTO MemoSubjects (rowid, SubjectKey, Memos)
            VALUES (-1, 'Horse:{{oldHorseId}}', '[]');
            INSERT INTO PredictionTickets
                (PredictionTicketId, RaceId, PredictorType, PredictorId, ConfidenceScore, SummaryComment,
                 PredictedAt, TicketStatus, Marks, Evaluations, EvaluationStatus)
            VALUES ('{{ticketId}}', '{{raceId}}', 'fixture', 'preserve-predictor', 0.5, 'preserve comment',
                    '2026-10-14T00:00:00+09:00', 0,
                    '[{"entryId":"{{oldEntryId}}","markCode":"A","predictedRank":1,"score":0.9,"comment":"preserve mark"}]',
                    '[]', 0);
            """);

        var beforeInvalidCommands = await stores.SourceSnapshotAsync();
        var missingGuid = await RunMigrationCommandAsync(stores, [HorseIdentityMigrationRuntimeMode.CommandName]);
        var malformedGuid = await RunMigrationCommandAsync(stores,
            [HorseIdentityMigrationRuntimeMode.CommandName, "not-a-guid"]);
        var extraArgument = await RunMigrationCommandAsync(stores,
            [HorseIdentityMigrationRuntimeMode.CommandName, Guid.NewGuid().ToString("D"), "unexpected"]);
        Assert.AreEqual(2, missingGuid.ExitCode);
        Assert.AreEqual(2, malformedGuid.ExitCode);
        Assert.AreEqual(2, extraArgument.ExitCode);
        Assert.AreEqual(beforeInvalidCommands, await stores.SourceSnapshotAsync(),
            "Invalid fixed-mode invocations must return before any configured source is read or modified.");
        await ReplaceSupportedEventsWithCommandBusSeedAsync(stores, oldHorseId, raceId, ticketId, oldEntryId);

        var manifest = new HorseIdentityMigrationManifest(
        [
            new HorseIdentityMigrationManifestEntry(oldHorseId, "jra-horse:2024100828", true),
        ]);
        var journal = new HorseIdentityMigrationJournal(Path.GetDirectoryName(stores.CollectionPath)!,
            [stores.EventStorePath, stores.CollectionPath, stores.SchedulePath]);
        var operationId = Guid.NewGuid();
        Assert.IsTrue((await journal.StageAsync(operationId, manifest, DateTimeOffset.UtcNow)).Succeeded);
        var collectionStore = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
        {
            StateDirectory = Path.GetDirectoryName(stores.CollectionPath)!,
            DatabaseFileName = Path.GetFileName(stores.CollectionPath),
        }));
        await collectionStore.SetPausedAsync(true, "standalone fixed CLI fixture",
            DateTimeOffset.UtcNow.AddSeconds(-901));

        var firstRun = await RunMigrationCommandAsync(stores,
            [HorseIdentityMigrationRuntimeMode.CommandName, operationId.ToString("D")]);
        Assert.AreEqual(0, firstRun.ExitCode,
            $"The fixed subcommand must execute the claimed operation through T10's owner-bound engine. " +
            $"Journal status/phase: {(await journal.GetStatusAsync(operationId))?.Status}/" +
            $"{(await journal.GetExecutionIntentAsync(operationId))?.Phase}; child output: {firstRun.Output}");
        Assert.IsFalse(firstRun.Output.Contains(stores.Root, StringComparison.OrdinalIgnoreCase),
            "CLI output must not disclose configured store paths.");
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.Succeeded,
            (await journal.GetStatusAsync(operationId))!.Status);
        var afterFirstRun = await stores.SourceSnapshotAsync();
        Assert.IsTrue(File.Exists(GetSourceGatePath(stores.EventStorePath)));
        Assert.IsTrue(File.Exists(GetSourceGatePath(stores.CollectionPath)));
        Assert.IsTrue(File.Exists(GetSourceGatePath(stores.SchedulePath)));
        Assert.AreNotEqual(beforeInvalidCommands, afterFirstRun);
        var destination = HorseIdentityMigrationManifestPolicy.Validate(manifest).Mappings.Single().DestinationHorseId;
        await using (var eventStore = new SqliteConnection($"Data Source={stores.EventStorePath};Mode=ReadOnly;Pooling=False"))
        {
            await eventStore.OpenAsync();
            await using var command = eventStore.CreateCommand();
            command.CommandText = "SELECT HorseId FROM Horses;";
            Assert.AreEqual(destination, await command.ExecuteScalarAsync());
        }

        File.WriteAllText(GetSourceGatePath(stores.EventStorePath), "corrupt gate proves receipt-only replay");
        var terminalRetry = await RunMigrationCommandAsync(stores,
            [HorseIdentityMigrationRuntimeMode.CommandName, operationId.ToString("D")]);
        Assert.AreEqual(0, terminalRetry.ExitCode,
            "A verified same-GUID retry returns the durable success receipt instead of being rejected as unstaged.");
        Assert.AreEqual(afterFirstRun, await stores.SourceSnapshotAsync(),
            "A terminal retry must not reopen or rewrite source stores.");
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.Succeeded,
            (await journal.GetStatusAsync(operationId))!.Status);
    }

    private static HorseIdentityMigrationManifest Manifest() => new(
    [
        new HorseIdentityMigrationManifestEntry(
            "horse-00000000-0000-0000-0000-000000000111", "jra-horse:2024100828", true),
    ]);

    internal static async Task ReplaceSupportedEventsWithCommandBusSeedAsync(
        HorseIdentityMigrationStoreEngineTests.StoreFixture stores, string horseId, string raceId,
        string ticketId, string entryId)
    {
        var provider = new SqliteDbContextProvider($"Data Source={stores.EventStorePath};Pooling=False");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(provider);
        services.AddSingleton<IDbContextProvider<EventStoreDbContext>>(
            serviceProvider => serviceProvider.GetRequiredService<SqliteDbContextProvider>());
        services.AddEventFlow(options => options
            .AddDefaults(typeof(HorseAggregate).Assembly)
            .AddDefaults(typeof(RaceAggregate).Assembly)
            .AddDefaults(typeof(RegisterHorseCommand).Assembly)
            .ConfigureEntityFramework(EntityFrameworkConfiguration.New)
            .UseEntityFrameworkEventStore<EventStoreDbContext>());
        try
        {
            await using (var context = provider.CreateContext())
                await context.Database.MigrateAsync();
            await stores.ExecuteAsync(stores.EventStorePath,
                "DELETE FROM EventEntity WHERE AggregateName IN ('HorseAggregate', 'PredictionTicketAggregate');");
            using var eventFlow = services.BuildServiceProvider();
            var commandBus = eventFlow.GetRequiredService<ICommandBus>();
            await commandBus.PublishAsync(new RegisterHorseCommand(new HorseId(horseId), "Fixture Horse", "FixtureHorse"),
                CancellationToken.None);
            var typedTicketId = new PredictionTicketId(ticketId);
            await commandBus.PublishAsync(new CreatePredictionTicketCommand(typedTicketId, raceId,
                "fixture", "preserve-predictor", .5m, "preserve summary"), CancellationToken.None);
            await commandBus.PublishAsync(new AddPredictionMarkCommand(typedTicketId, entryId, "A", 1, .9m,
                "preserve mark"), CancellationToken.None);
        }
        finally { provider.Dispose(); }
    }

    private static string GetSourceGatePath(string sourcePath) =>
        Path.Combine(Path.GetDirectoryName(sourcePath)!, $".{Path.GetFileName(sourcePath)}.horse-identity-migration.lock");

    private static async Task<CliResult> RunMigrationCommandAsync(
        HorseIdentityMigrationStoreEngineTests.StoreFixture stores, IReadOnlyList<string> arguments,
        TimeSpan? timeout = null, bool configureIsolatedAws = false)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = stores.Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
            EnableRaisingEvents = true,
        };
        process.StartInfo.ArgumentList.Add(typeof(HorseIdentityMigrationRuntimeMode).Assembly.Location);
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.Environment["ConnectionStrings__EventStore"] = $"Data Source={stores.EventStorePath};Pooling=False";
        process.StartInfo.Environment["CollectionPlatform__StateDirectory"] = Path.GetDirectoryName(stores.CollectionPath)!;
        process.StartInfo.Environment["CollectionPlatform__DatabaseFileName"] = Path.GetFileName(stores.CollectionPath);
        process.StartInfo.Environment["PredictionScheduling__StateDirectory"] = Path.GetDirectoryName(stores.SchedulePath)!;
        process.StartInfo.Environment["PredictionScheduling__StoreFileName"] = Path.GetFileName(stores.SchedulePath);
        process.StartInfo.Environment["CollectionQueue__Provider"] = "Sqs";
        if (configureIsolatedAws)
        {
            process.StartInfo.Environment["AWS_REGION"] = "ap-northeast-1";
            process.StartInfo.Environment["AWS_EC2_METADATA_DISABLED"] = "true";
        }
        process.StartInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        process.StartInfo.Environment["DOTNET_NOLOGO"] = "1";

        var output = new StringBuilder(8192);
        var outputLock = new object();
        void Capture(string? line)
        {
            if (line is null) return;
            lock (outputLock)
            {
                if (output.Length >= 8192) return;
                output.Append(line.AsSpan(0, Math.Min(line.Length, 8192 - output.Length))).AppendLine();
            }
        }
        process.OutputDataReceived += (_, eventArgs) => Capture(eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => Capture(eventArgs.Data);
        if (!process.Start()) throw new InvalidOperationException("The migration CLI child did not start.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.StandardInput.Close();
        var timedOut = false;
        var processId = process.Id;
        try
        {
            using var timeoutSource = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(90));
            try { await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                timedOut = true;
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
            }
            process.WaitForExit();
            lock (outputLock) return new(process.ExitCode, output.ToString(), timedOut, processId, process.HasExited);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
            }
            process.Dispose();
        }
    }

    private sealed record CliResult(int ExitCode, string Output, bool TimedOut = false, int ProcessId = 0,
        bool ExitedAfterCleanup = true);

    private sealed class RuntimeFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "hrp-horse-id-runtime-tests", Guid.NewGuid().ToString("N"));
        private readonly int _port;

        internal RuntimeFixture()
        {
            Directory.CreateDirectory(_root);
            StateDirectory = Path.Combine(_root, "appstate");
            Directory.CreateDirectory(StateDirectory);
            EventStorePath = Path.Combine(_root, "event.db");
            CollectionStorePath = Path.Combine(StateDirectory, "collection-platform.db");
            ScheduleStorePath = Path.Combine(_root, "schedule", "prediction-executions.db");
            QueueStorePath = Path.Combine(_root, "queue.db");
            ApiKey = "test-only-" + Guid.NewGuid().ToString("N");
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            _port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            Journal = new HorseIdentityMigrationJournal(StateDirectory,
                [EventStorePath, CollectionStorePath, ScheduleStorePath, QueueStorePath]);
        }

        internal string StateDirectory { get; }
        internal string EventStorePath { get; }
        internal string CollectionStorePath { get; }
        internal string ScheduleStorePath { get; }
        internal string QueueStorePath { get; }
        internal string ApiKey { get; }
        internal HorseIdentityMigrationJournal Journal { get; }

        internal Process StartApiProcess(bool maintenanceMode = false)
        {
            var apiAssembly = typeof(HorseIdentityMigrationRuntimeMode).Assembly.Location;
            var process = new Process
            {
                StartInfo = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = FindRepositoryRoot(),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
                EnableRaisingEvents = true,
            };
            process.StartInfo.ArgumentList.Add(apiAssembly);
            process.StartInfo.ArgumentList.Add("--urls");
            process.StartInfo.ArgumentList.Add($"http://127.0.0.1:{_port}");
            process.StartInfo.Environment["CollectionPlatform__StateDirectory"] = StateDirectory;
            process.StartInfo.Environment["CollectionPlatform__MaintenanceMode"] = maintenanceMode ? "true" : "false";
            process.StartInfo.Environment["ConnectionStrings__EventStore"] = $"Data Source={EventStorePath}";
            process.StartInfo.Environment["PredictionScheduling__StateDirectory"] = Path.GetDirectoryName(ScheduleStorePath)!;
            process.StartInfo.Environment["PredictionScheduling__StoreFileName"] = Path.GetFileName(ScheduleStorePath);
            process.StartInfo.Environment["CollectionQueue__Provider"] = "Local";
            process.StartInfo.Environment["CollectionQueue__LocalDatabasePath"] = QueueStorePath;
            process.StartInfo.Environment["ApiKey__HeaderName"] = "X-Test-Horse-Key";
            process.StartInfo.Environment["ApiKey__Key"] = ApiKey;
            process.StartInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            process.StartInfo.Environment["DOTNET_NOLOGO"] = "1";
            if (!process.Start()) throw new InvalidOperationException("API child process did not start.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.StandardInput.Close();
            return process;
        }

        internal async Task<HttpClient> WaitForHealthAsync(Process process)
        {
            var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_port}") };
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!deadline.IsCancellationRequested)
            {
                if (process.HasExited)
                {
                    client.Dispose();
                    throw new InvalidOperationException($"API child exited before health was ready ({process.ExitCode}).");
                }
                try
                {
                    using var response = await client.GetAsync("/health", deadline.Token);
                    if (response.IsSuccessStatusCode) return client;
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) when (!deadline.IsCancellationRequested) { }
                await Task.Delay(100, deadline.Token);
            }
            client.Dispose();
            throw new TimeoutException("API child did not serve the bounded health endpoint.");
        }

        internal static async Task StopAsync(Process process)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(timeout.Token);
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HorseRacingPrediction.sln")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class StubServer : IServer
    {
        public IFeatureCollection Features { get; } = new FeatureCollection();
        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
            where TContext : notnull => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() { }
    }
}
