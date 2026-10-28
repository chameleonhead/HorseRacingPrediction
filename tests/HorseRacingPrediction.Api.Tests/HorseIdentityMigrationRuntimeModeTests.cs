using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using EventFlow;
using EventFlow.EntityFramework;
using EventFlow.EntityFramework.Extensions;
using EventFlow.Extensions;
using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.Api.IdentityMigration;
using HorseRacingPrediction.Api.Endpoints.IdentityMigration;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityMigrationRuntimeModeTests
{
    [TestMethod]
    public async Task PendingPublicStatusRetainsKindAndManifestAfterSeal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "horse-migration-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var manifest = Manifest();
            var requestHash = HorseIdentityMigrationExecutionPolicy.ComputePendingImmutableRequestHash(manifest);
            var operationId = Guid.NewGuid();
            var journal = new HorseIdentityMigrationJournal(directory, []);
            Assert.IsTrue((await journal.StagePendingWithRequestKeyAsync(operationId, manifest, Guid.NewGuid(),
                requestHash, DateTimeOffset.UtcNow)).Succeeded);
            Assert.IsTrue((await journal.CreateExecutionIntentAsync(operationId, requestHash,
                DateTimeOffset.UtcNow)).Succeeded);
            foreach (var phase in new[]
            {
                HorseIdentityMigrationExecutionPhase.Preparing,
                HorseIdentityMigrationExecutionPhase.Draining,
                HorseIdentityMigrationExecutionPhase.StoppingApi,
                HorseIdentityMigrationExecutionPhase.ApiExited,
                HorseIdentityMigrationExecutionPhase.BackedUp,
            })
                Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId, phase,
                    DateTimeOffset.UtcNow)).Succeeded);
            Assert.IsTrue((await journal.SealPendingRequestAsync(operationId, requestHash,
                new string('1', 64), new string('2', 64), new string('3', 64), DateTimeOffset.UtcNow)).Succeeded);

            var saved = await journal.GetSavedRequestAsync(operationId);
            var pending = await journal.GetPendingRequestAsync(operationId);
            var intent = await journal.GetExecutionIntentAsync(operationId);
            var response = HorseIdentityMigrationEndpoints.ToPublic(
                (await journal.GetStatusAsync(operationId))!, saved, pending, intent);

            Assert.AreEqual(HorseIdentityMigrationAdmissionKind.PendingV2, response.RequestKind);
            Assert.IsNotNull(response.SavedManifest);
            Assert.IsNull(response.PreviewFingerprint);
            Assert.AreEqual(HorseIdentityMigrationExecutionPhase.BackedUp.ToString(), response.ExecutionPhase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void OperatorOnlyModeForcesTheSupervisorRootToSelectBlockedHostWhenReady()
    {
        Assert.IsTrue(HorseIdentityMigrationSupervisor.ShouldStartBlockedHost(
            HorseIdentityMigrationStartupState.Ready, hasActiveIntent: false, [], forceBlocked: true));
    }

    [TestMethod]
    public void OperatorOnlyMalformedOrUnboundTargetRemainsForcedClosed()
    {
        var priorFlag = Environment.GetEnvironmentVariable(HorseIdentityMigrationRuntimeMode.OperatorOnlyEnvironmentVariable);
        var priorTarget = Environment.GetEnvironmentVariable(HorseIdentityMigrationRuntimeMode.OperatorOperationIdEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(HorseIdentityMigrationRuntimeMode.OperatorOnlyEnvironmentVariable, "true");
            Environment.SetEnvironmentVariable(HorseIdentityMigrationRuntimeMode.OperatorOperationIdEnvironmentVariable, null);
            Assert.IsTrue(HorseIdentityMigrationRuntimeMode.IsOperatorOnlyMode(), "A forced host without a bound GUID stays blocked.");

            Environment.SetEnvironmentVariable(HorseIdentityMigrationRuntimeMode.OperatorOnlyEnvironmentVariable, "true");
            Environment.SetEnvironmentVariable(HorseIdentityMigrationRuntimeMode.OperatorOperationIdEnvironmentVariable, "not-a-guid");
            Assert.IsTrue(HorseIdentityMigrationRuntimeMode.IsOperatorOnlyMode(), "A malformed target cannot release forced mode.");

            Environment.SetEnvironmentVariable(HorseIdentityMigrationRuntimeMode.OperatorOnlyEnvironmentVariable, "unexpected");
            Environment.SetEnvironmentVariable(HorseIdentityMigrationRuntimeMode.OperatorOperationIdEnvironmentVariable, null);
            Assert.IsTrue(HorseIdentityMigrationRuntimeMode.IsOperatorOnlyMode(), "Malformed force state fails closed.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(HorseIdentityMigrationRuntimeMode.OperatorOnlyEnvironmentVariable, priorFlag);
            Environment.SetEnvironmentVariable(HorseIdentityMigrationRuntimeMode.OperatorOperationIdEnvironmentVariable, priorTarget);
        }
    }

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
    public void MinimalOperatorHtmlPrioritizesSafeFailureWhenResultListIsBounded()
    {
        var operation = new HorseIdentityMigrationOperationResponse(Guid.NewGuid(), "Failed", "BackupFailed",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            Enumerable.Repeat(new HorseIdentityMigrationOperationResult("PlanIncomplete", null, 0), 32)
                .Append(new("BackupFailed", null, 0)).ToArray());

        var html = HorseIdentityMigrationRuntimeMode.BuildMinimalOperatorHtml("/races", operation, journalAvailable: true);

        StringAssert.Contains(html, "BackupFailed");
        Assert.AreEqual(1, html.Split("<li>BackupFailed</li>", StringSplitOptions.None).Length - 1);
        Assert.AreEqual(31, html.Split("<li>PlanIncomplete</li>", StringSplitOptions.None).Length - 1);
    }

    [TestMethod]
    public void OperatorHealthProbeSupportsDeploymentWildcardAndSemicolonBindings()
    {
        var health = HorseIdentityMigrationSupervisor.CreateOperatorHealthUri(
            "http://+:8080;http://*:8081");

        Assert.AreEqual("http", health.Scheme);
        Assert.AreEqual("127.0.0.1", health.Host);
        Assert.AreEqual(8080, health.Port);
        Assert.AreEqual("/health", health.AbsolutePath);

        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["http_ports"] = "8080;8081" })
            .Build();
        Assert.AreEqual("http://+:8080;http://+:8081",
            HorseIdentityMigrationSupervisor.ResolveOperatorUrls(configuration));
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
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
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

            foreach (var path in new[] { "/", "/races", "/jobs", "/settings", "/settings/horse-id-migration" })
            {
                using var page = await client.GetAsync(path);
                Assert.AreEqual(HttpStatusCode.OK, page.StatusCode, path);
                Assert.AreEqual("no-store, no-cache, max-age=0", page.Headers.CacheControl?.ToString(), path);
                var pageHtml = await page.Content.ReadAsStringAsync();
                StringAssert.Contains(pageHtml, "Partial", path);
                StringAssert.Contains(pageHtml, operationId.ToString("D"), path);
                StringAssert.Contains(pageHtml, "状態を更新", path);
                StringAssert.Contains(pageHtml, $"/settings/horse-id-migration?operationId={operationId:D}", path);
                Assert.DoesNotContain(fixture.Root, pageHtml, StringComparison.OrdinalIgnoreCase, path);
                Assert.DoesNotContain(fixture.ApiKey, pageHtml, StringComparison.Ordinal, path);
            }

            using var injected = await client.GetAsync("/races?message=%3Cscript%3Ealert(1)%3C%2Fscript%3E");
            Assert.AreEqual(HttpStatusCode.OK, injected.StatusCode);
            Assert.DoesNotContain("<script>alert(1)</script>", await injected.Content.ReadAsStringAsync(),
                StringComparison.OrdinalIgnoreCase);
            Assert.AreEqual(HorseIdentityMigrationOperationStatus.PartialUnverified,
                (await fixture.Journal.GetStatusAsync(operationId))!.Status);

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
            using var htmlNegotiation = new HttpRequestMessage(HttpMethod.Get, "/races");
            htmlNegotiation.Headers.Accept.ParseAdd("application/json");
            using var nonHtml = await client.SendAsync(htmlNegotiation);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, nonHtml.StatusCode);
            StringAssert.Contains(nonHtml.Content.Headers.ContentType?.MediaType ?? string.Empty, "problem");
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
    public async Task OperatorOnlyFlagKeepsReadyAndUnknownJournalOnAuthenticatedBlockedSurface()
    {
        foreach (var corruptJournal in new[] { false, true })
        {
            using var fixture = new RuntimeFixture();
            if (corruptJournal)
                await File.WriteAllBytesAsync(fixture.Journal.DatabasePath, Encoding.ASCII.GetBytes("corrupt journal"));

            using var child = fixture.StartApiProcess(operatorOnly: true);
            try
            {
                using var client = await fixture.WaitForHealthAsync(child);
                client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
                using var login = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["username"] = "user",
                    ["password"] = fixture.ApiKey,
                    ["ReturnUrl"] = "/",
                }));
                Assert.AreEqual(HttpStatusCode.OK, login.StatusCode,
                    $"Operator-only mode must serve authenticated navigation ({corruptJournal}).");
                StringAssert.Contains(await login.Content.ReadAsStringAsync(), "Status: <strong>Unknown</strong>",
                    $"Operator-only mode must expose unknown status safely ({corruptJournal}).");

                client.DefaultRequestHeaders.Add("X-Test-Horse-Key", fixture.ApiKey);
                using var businessApi = await client.GetAsync("/api/races/not-available");
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, businessApi.StatusCode,
                    $"Business API must remain blocked ({corruptJournal}).");
                using var execute = await client.PostAsync(
                    $"/api/v2/admin/horse-id-migration-operations/{Guid.NewGuid():D}/execute",
                    new StringContent(string.Empty));
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, execute.StatusCode,
                    $"Migration apply must remain unavailable ({corruptJournal}).");
                using var verify = await client.PostAsync(
                    $"/settings/horse-id-migration/{Guid.NewGuid():D}/verify", new StringContent(string.Empty));
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, verify.StatusCode,
                    $"Migration verification must remain unavailable ({corruptJournal}).");

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
    }

    [TestMethod]
    public async Task ForcedOperatorCancellationApiUsesApiKeyWithoutCookieAndRejectsCookieOnly()
    {
        using var fixture = new RuntimeFixture();
        var operationId = Guid.NewGuid();
        var manifest = Manifest();
        var fingerprint = new string('E', 64);
        var requestHash = HorseIdentityMigrationExecutionPolicy.ComputeImmutableRequestHash(manifest, fingerprint);
        Assert.IsTrue((await fixture.Journal.StageWithRequestKeyAsync(operationId, manifest, Guid.NewGuid(), requestHash,
            fingerprint, DateTimeOffset.UtcNow)).Succeeded);
        Assert.IsTrue((await fixture.Journal.CreateExecutionIntentAsync(operationId, requestHash,
            DateTimeOffset.UtcNow)).Succeeded);
        foreach (var phase in new[]
        {
            HorseIdentityMigrationExecutionPhase.Preparing,
            HorseIdentityMigrationExecutionPhase.Draining,
            HorseIdentityMigrationExecutionPhase.StoppingApi,
        })
            Assert.IsTrue((await fixture.Journal.AdvanceExecutionIntentAsync(operationId, phase,
                DateTimeOffset.UtcNow)).Succeeded);

        using var child = fixture.StartApiProcess(operatorOnly: true, operatorOperationId: operationId.ToString("D"));
        try
        {
            using var healthClient = await fixture.WaitForHealthAsync(child);
            var route = $"/api/v2/admin/horse-id-migration-operations/{operationId:D}/cancel-preparation";
            using var noCookieHandler = new HttpClientHandler { AllowAutoRedirect = false };
            using var noCookieClient = new HttpClient(noCookieHandler) { BaseAddress = new Uri(fixture.OperatorUrl) };
            using var missingKey = await noCookieClient.PostAsync(route, content: null);
            Assert.AreEqual(HttpStatusCode.Unauthorized, missingKey.StatusCode,
                "The forced-host API route must retain the API-key middleware contract.");
            noCookieClient.DefaultRequestHeaders.Add("X-Test-Horse-Key", "invalid-test-key");
            using var invalidKey = await noCookieClient.PostAsync(route, content: null);
            Assert.AreEqual(HttpStatusCode.Unauthorized, invalidKey.StatusCode);
            Assert.AreEqual(HorseIdentityMigrationExecutionPhase.StoppingApi,
                (await fixture.Journal.GetExecutionIntentAsync(operationId))!.Phase);

            var cookies = new CookieContainer();
            using var cookieHandler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = cookies };
            using var cookieClient = new HttpClient(cookieHandler) { BaseAddress = new Uri(fixture.OperatorUrl) };
            using var login = await cookieClient.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = "user",
                ["password"] = fixture.ApiKey,
                ["ReturnUrl"] = "/",
            }));
            Assert.AreEqual(HttpStatusCode.Redirect, login.StatusCode);
            using var cookieOnly = await cookieClient.PostAsync(route, content: null);
            Assert.AreEqual(HttpStatusCode.Unauthorized, cookieOnly.StatusCode,
                "An authenticated UI cookie cannot replace the API key on the bodyless JSON route.");
            Assert.AreEqual(HorseIdentityMigrationExecutionPhase.StoppingApi,
                (await fixture.Journal.GetExecutionIntentAsync(operationId))!.Phase);

            using var apiKeyOnlyHandler = new HttpClientHandler { AllowAutoRedirect = false };
            using var apiKeyOnlyClient = new HttpClient(apiKeyOnlyHandler) { BaseAddress = new Uri(fixture.OperatorUrl) };
            apiKeyOnlyClient.DefaultRequestHeaders.Add("X-Test-Horse-Key", fixture.ApiKey);
            using var accepted = await apiKeyOnlyClient.PostAsync(route, content: null);
            Assert.AreEqual(HttpStatusCode.Accepted, accepted.StatusCode,
                "The API-key-only integration used by callers must not be redirected to the cookie login page.");
            Assert.AreEqual(HorseIdentityMigrationExecutionPhase.CancellationRequested,
                (await fixture.Journal.GetExecutionIntentAsync(operationId))!.Phase);
        }
        finally
        {
            await RuntimeFixture.StopAsync(child);
        }
    }

    [TestMethod]
    public async Task ForcedOperatorHostExposesOnlyItsBoundCancellationFormWithoutReadonlyVerification()
    {
        var root = Path.Combine(Path.GetTempPath(), "horse-migration-bound-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var priorFlag = Environment.GetEnvironmentVariable(HorseIdentityMigrationRuntimeMode.OperatorOnlyEnvironmentVariable);
        var priorTarget = Environment.GetEnvironmentVariable(HorseIdentityMigrationRuntimeMode.OperatorOperationIdEnvironmentVariable);
        try
        {
            var journal = new HorseIdentityMigrationJournal(Path.Combine(root, "journal"), []);
            var operationId = Guid.NewGuid();
            var manifest = Manifest();
            var fingerprint = new string('A', 64);
            var requestHash = HorseIdentityMigrationExecutionPolicy.ComputeImmutableRequestHash(manifest, fingerprint);
            Assert.IsTrue((await journal.StageWithRequestKeyAsync(operationId, manifest, Guid.NewGuid(), requestHash,
                fingerprint, DateTimeOffset.UtcNow)).Succeeded);
            Assert.IsTrue((await journal.CreateExecutionIntentAsync(operationId, requestHash, DateTimeOffset.UtcNow)).Succeeded);
            foreach (var phase in new[]
            {
                HorseIdentityMigrationExecutionPhase.Preparing,
                HorseIdentityMigrationExecutionPhase.Draining,
                HorseIdentityMigrationExecutionPhase.StoppingApi,
            })
                Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId, phase, DateTimeOffset.UtcNow)).Succeeded);

            Environment.SetEnvironmentVariable(HorseIdentityMigrationRuntimeMode.OperatorOnlyEnvironmentVariable, "true");
            Environment.SetEnvironmentVariable(HorseIdentityMigrationRuntimeMode.OperatorOperationIdEnvironmentVariable,
                operationId.ToString("D"));
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.Configure<ApiKeyOptions>(options => options.Key = "test-only-key");
            builder.Services.AddAdminAuthentication();
            HorseIdentityMigrationRuntimeMode.ConfigureMinimalOperatorCookieChallenges(builder.Services);
            builder.Services.AddAntiforgery();
            var app = builder.Build();
            app.UseApiKeyProtection();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseAntiforgery();
            app.MapAdminLoginEndpoints();
            HorseIdentityMigrationRuntimeMode.MapReadonlyVerificationEndpoints(app, journal,
                allowVerificationActions: false, allowPreparationCancellationActions: true,
                boundOperatorOperationId: operationId);
            await app.StartAsync();
            try
            {
                using var client = app.GetTestClient();
                using var login = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["username"] = "user",
                    ["password"] = "test-only-key",
                    ["ReturnUrl"] = "/",
                }));
                Assert.AreEqual(HttpStatusCode.Redirect, login.StatusCode);
                var authCookie = login.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
                using var statusRequest = new HttpRequestMessage(HttpMethod.Get,
                    $"/settings/horse-id-migration?operationId={operationId:D}");
                statusRequest.Headers.Add("Cookie", authCookie);
                using var status = await client.SendAsync(statusRequest);
                Assert.AreEqual(HttpStatusCode.OK, status.StatusCode);
                var html = await status.Content.ReadAsStringAsync();
                StringAssert.Contains(html, "confirmCancellation");
                Assert.IsFalse(html.Contains("Run read-only verification", StringComparison.Ordinal));
                var token = System.Text.RegularExpressions.Regex.Match(html,
                    "name=\\\"__RequestVerificationToken\\\" value=\\\"([^\\\"]+)\\\"").Groups[1].Value;
                Assert.IsFalse(string.IsNullOrWhiteSpace(token));
                var antiforgeryCookie = status.Headers.GetValues("Set-Cookie").Single(value =>
                    !value.StartsWith("HorseRacingPrediction.Admin=", StringComparison.Ordinal)).Split(';')[0];

                var otherOperation = Guid.NewGuid();
                using var denied = new HttpRequestMessage(HttpMethod.Post,
                    $"/settings/horse-id-migration/{otherOperation:D}/cancel-preparation")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["__RequestVerificationToken"] = token,
                        ["confirmCancellation"] = "cancel-preparation",
                    }),
                };
                denied.Headers.Add("Cookie", authCookie + "; " + antiforgeryCookie);
                Assert.AreEqual(HttpStatusCode.NotFound, (await client.SendAsync(denied)).StatusCode);
                Assert.AreEqual(HorseIdentityMigrationExecutionPhase.StoppingApi,
                    (await journal.GetExecutionIntentAsync(operationId))!.Phase);

                using var accepted = new HttpRequestMessage(HttpMethod.Post,
                    $"/settings/horse-id-migration/{operationId:D}/cancel-preparation")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["__RequestVerificationToken"] = token,
                        ["confirmCancellation"] = "cancel-preparation",
                    }),
                };
                accepted.Headers.Add("Cookie", authCookie + "; " + antiforgeryCookie);
                Assert.AreEqual(HttpStatusCode.Accepted, (await client.SendAsync(accepted)).StatusCode);
                Assert.AreEqual(HorseIdentityMigrationExecutionPhase.CancellationRequested,
                    (await journal.GetExecutionIntentAsync(operationId))!.Phase);
            }
            finally { await app.DisposeAsync(); }
        }
        finally
        {
            Environment.SetEnvironmentVariable(HorseIdentityMigrationRuntimeMode.OperatorOnlyEnvironmentVariable, priorFlag);
            Environment.SetEnvironmentVariable(HorseIdentityMigrationRuntimeMode.OperatorOperationIdEnvironmentVariable, priorTarget);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task FailedPreApplyOperationWithStalePreparationPhaseDoesNotOfferCancellationForm()
    {
        var root = Path.Combine(Path.GetTempPath(), "horse-migration-failed-cancel-form-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var priorFlag = Environment.GetEnvironmentVariable(HorseIdentityMigrationRuntimeMode.OperatorOnlyEnvironmentVariable);
        var priorTarget = Environment.GetEnvironmentVariable(HorseIdentityMigrationRuntimeMode.OperatorOperationIdEnvironmentVariable);
        try
        {
            var journal = new HorseIdentityMigrationJournal(Path.Combine(root, "journal"), []);
            var operationId = Guid.NewGuid();
            var manifest = Manifest();
            var fingerprint = new string('B', 64);
            var requestHash = HorseIdentityMigrationExecutionPolicy.ComputeImmutableRequestHash(manifest, fingerprint);
            Assert.IsTrue((await journal.StageWithRequestKeyAsync(operationId, manifest, Guid.NewGuid(), requestHash,
                fingerprint, DateTimeOffset.UtcNow)).Succeeded);
            Assert.IsTrue((await journal.CreateExecutionIntentAsync(operationId, requestHash, DateTimeOffset.UtcNow)).Succeeded);
            foreach (var phase in new[]
            {
                HorseIdentityMigrationExecutionPhase.Preparing,
                HorseIdentityMigrationExecutionPhase.Draining,
                HorseIdentityMigrationExecutionPhase.StoppingApi,
            })
                Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId, phase, DateTimeOffset.UtcNow)).Succeeded);
            Assert.IsTrue((await journal.RecordFailureAsync(operationId, HorseIdentityMigrationResultCode.PreparationFailed,
                DateTimeOffset.UtcNow)).Succeeded);

            Environment.SetEnvironmentVariable(HorseIdentityMigrationRuntimeMode.OperatorOnlyEnvironmentVariable, "true");
            Environment.SetEnvironmentVariable(HorseIdentityMigrationRuntimeMode.OperatorOperationIdEnvironmentVariable,
                operationId.ToString("D"));
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.Configure<ApiKeyOptions>(options => options.Key = "test-only-key");
            builder.Services.AddAdminAuthentication();
            HorseIdentityMigrationRuntimeMode.ConfigureMinimalOperatorCookieChallenges(builder.Services);
            builder.Services.AddAntiforgery();
            var app = builder.Build();
            app.UseApiKeyProtection();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseAntiforgery();
            app.MapAdminLoginEndpoints();
            HorseIdentityMigrationRuntimeMode.MapReadonlyVerificationEndpoints(app, journal,
                allowVerificationActions: false, allowPreparationCancellationActions: true,
                boundOperatorOperationId: operationId);
            await app.StartAsync();
            try
            {
                using var client = app.GetTestClient();
                using var login = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["username"] = "user",
                    ["password"] = "test-only-key",
                    ["ReturnUrl"] = "/",
                }));
                Assert.AreEqual(HttpStatusCode.Redirect, login.StatusCode);
                var authCookie = login.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
                using var statusRequest = new HttpRequestMessage(HttpMethod.Get,
                    $"/settings/horse-id-migration?operationId={operationId:D}");
                statusRequest.Headers.Add("Cookie", authCookie);
                using var status = await client.SendAsync(statusRequest);
                Assert.AreEqual(HttpStatusCode.OK, status.StatusCode);
                var html = await status.Content.ReadAsStringAsync();
                Assert.IsFalse(html.Contains("confirmCancellation", StringComparison.Ordinal),
                    "A stale pre-apply phase is not sufficient once the operation is generically Failed.");
                StringAssert.Contains(html, "PreparationFailed");
            }
            finally { await app.DisposeAsync(); }
        }
        finally
        {
            Environment.SetEnvironmentVariable(HorseIdentityMigrationRuntimeMode.OperatorOnlyEnvironmentVariable, priorFlag);
            Environment.SetEnvironmentVariable(HorseIdentityMigrationRuntimeMode.OperatorOperationIdEnvironmentVariable, priorTarget);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task SupervisorKeepsJournalOnlyOperatorAvailableDuringApplyAndVerificationWork()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Inconclusive("The owned-child lifecycle proof requires Linux process ownership semantics.");

        using var fixture = new RuntimeFixture();
        var sources = new HorseIdentityMigrationConfiguredSources(fixture.EventStorePath,
            fixture.CollectionStorePath, fixture.ScheduleStorePath, fixture.QueueStorePath);
        using var controlLease = HorseIdentityMigrationMaintenanceOwnership.AcquireSupervisorControlLease(
            sources.PhysicalSourcePaths);
        using var environment = new OperatorTestEnvironment(fixture);

        foreach (var workWindow in new[] { "apply", "verification" })
        {
            var workStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var operatorProcessIds = new System.Collections.Concurrent.ConcurrentQueue<int>();
            var restartObserved = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var restartExpected = 0;
            var workTask = HorseIdentityMigrationSupervisor.RunWorkWithOperatorHostAsync(async cancellationToken =>
            {
                workStarted.TrySetResult();
                await releaseWork.Task.WaitAsync(cancellationToken);
                return true;
            }, controlLease, fixture.OperatorUrls, CancellationToken.None,
                processId =>
                {
                    operatorProcessIds.Enqueue(processId);
                    if (Volatile.Read(ref restartExpected) != 0) restartObserved.TrySetResult(processId);
                });

            try
            {
                await workStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.IsTrue(operatorProcessIds.TryPeek(out _),
                    $"The journal-only operator host must be owned before {workWindow} work starts.");
                var activeOperatorProcessId = operatorProcessIds.Last();
                if (workWindow == "apply")
                {
                    Volatile.Write(ref restartExpected, 1);
                    using (var firstOperator = Process.GetProcessById(activeOperatorProcessId))
                        firstOperator.Kill(entireProcessTree: false);
                    var restartedOperatorProcessId = await restartObserved.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    Assert.AreNotEqual(activeOperatorProcessId, restartedOperatorProcessId,
                        "An operator crash during owned apply work must restart only the journal-only child.");
                    using var restartedOperator = Process.GetProcessById(restartedOperatorProcessId);
                    using var healthClient = await fixture.WaitForHealthAsync(restartedOperator);
                }
                using var handler = new HttpClientHandler { CookieContainer = new CookieContainer() };
                using var client = new HttpClient(handler) { BaseAddress = new Uri(fixture.OperatorUrl) };
                client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
                using var login = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["username"] = "user",
                    ["password"] = fixture.ApiKey,
                    ["ReturnUrl"] = "/",
                }));
                Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);

                foreach (var path in new[] { "/", "/races", "/jobs", "/settings" })
                {
                    using var response = await client.GetAsync(path);
                    Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
                        $"Authenticated {workWindow} navigation to {path} must remain available.");
                    StringAssert.Contains(await response.Content.ReadAsStringAsync(), "Status: <strong>Unknown</strong>",
                        $"{path} should render the bounded journal-only status instead of the application service.");
                }

                using var canonical = await client.GetAsync("/settings/horse-id-migration");
                Assert.AreEqual(HttpStatusCode.OK, canonical.StatusCode,
                    "The canonical operator status route must work without an operation query.");
                using var verifyPost = await client.PostAsync(
                    $"/settings/horse-id-migration/{Guid.NewGuid():D}/verify", new StringContent(string.Empty));
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, verifyPost.StatusCode,
                    "The temporary operator child must not expose an action that can start verification during active work.");

                Assert.IsFalse(File.Exists(fixture.EventStorePath));
                Assert.IsFalse(File.Exists(fixture.CollectionStorePath));
                Assert.IsFalse(File.Exists(fixture.ScheduleStorePath));
                Assert.IsFalse(File.Exists(fixture.QueueStorePath));
            }
            finally
            {
                releaseWork.TrySetResult();
                try { await workTask.WaitAsync(TimeSpan.FromSeconds(30)); }
                catch { /* Preserve the original assertion while bounding owned-child cleanup. */ }
            }

            Assert.IsTrue(await workTask.WaitAsync(TimeSpan.FromSeconds(30)),
                $"The {workWindow} work delegate should complete successfully.");
            foreach (var processId in operatorProcessIds)
            {
                try
                {
                    using var exitedOperator = Process.GetProcessById(processId);
                    Assert.IsTrue(exitedOperator.HasExited,
                        "Every temporary operator child must be terminated and reaped before normal API restart.");
                }
                catch (ArgumentException)
                {
                    // The operating system already removed this reaped child process.
                }
            }
        }

        var failedOperatorProcessId = 0;
        try
        {
            _ = await HorseIdentityMigrationSupervisor.RunWorkWithOperatorHostAsync(
                _ => Task.FromException<bool>(new InvalidOperationException("controlled work failure")),
                controlLease, fixture.OperatorUrls, CancellationToken.None,
                processId => Interlocked.Exchange(ref failedOperatorProcessId, processId));
            Assert.Fail("A failing work delegate must propagate after operator cleanup.");
        }
        catch (InvalidOperationException exception)
        {
            Assert.AreEqual("controlled work failure", exception.Message);
        }
        try
        {
            using var exitedOperator = Process.GetProcessById(failedOperatorProcessId);
            Assert.IsTrue(exitedOperator.HasExited,
                "The operator child must be reaped even when the active work delegate faults.");
        }
        catch (ArgumentException)
        {
            // The operating system already removed the reaped child process.
        }
    }

    [TestMethod]
    public async Task CorruptJournalKeepsAuthenticatedOperatorPageAvailableAsUnknownWithoutOpeningBusinessStores()
    {
        using var fixture = new RuntimeFixture();
        var operationId = Guid.NewGuid();
        Assert.IsTrue((await fixture.Journal.StageAsync(operationId, Manifest(), DateTimeOffset.UtcNow)).Succeeded);
        await File.WriteAllBytesAsync(fixture.Journal.DatabasePath, Encoding.ASCII.GetBytes("corrupt journal contents"));

        using var child = fixture.StartApiProcess();
        try
        {
            using var client = await fixture.WaitForHealthAsync(child);
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
            using var login = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = "user",
                ["password"] = fixture.ApiKey,
                ["ReturnUrl"] = "/races",
            }));
            Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);

            using var page = await client.GetAsync("/races");
            Assert.AreEqual(HttpStatusCode.OK, page.StatusCode);
            Assert.AreEqual("no-store, no-cache, max-age=0", page.Headers.CacheControl?.ToString());
            var html = await page.Content.ReadAsStringAsync();
            StringAssert.Contains(html, "Unavailable");
            Assert.DoesNotContain(operationId.ToString("D"), html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("corrupt journal contents", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SqliteException", html, StringComparison.OrdinalIgnoreCase);
            using var canonicalPage = await client.GetAsync("/settings/horse-id-migration");
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, canonicalPage.StatusCode);
            Assert.AreEqual("text/html", canonicalPage.Content.Headers.ContentType?.MediaType);
            var canonicalHtml = await canonicalPage.Content.ReadAsStringAsync();
            StringAssert.Contains(canonicalHtml, "Unknown");
            StringAssert.Contains(canonicalHtml, "Unavailable");
            Assert.DoesNotContain(operationId.ToString("D"), canonicalHtml, StringComparison.OrdinalIgnoreCase);
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
    public async Task DeployHelperSourceLeaseInteroperatesWithRealApiOnLinux()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Inconclusive("The deploy helper uses the Linux source-lock protocol used by the production runner.");

        using var fixture = new RuntimeFixture();
        var sources = new[]
        {
            fixture.EventStorePath, fixture.CollectionStorePath, fixture.ScheduleStorePath, fixture.QueueStorePath,
        };
        foreach (var source in sources)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            await File.WriteAllBytesAsync(source, []);
        }
        var sourceHashes = new Dictionary<string, string>(StringComparer.Ordinal);

        using (var api = fixture.StartApiProcess(maintenanceMode: true))
        {
            try
            {
                using var client = await fixture.WaitForHealthAsync(api);
                using var swagger = await client.GetAsync("/swagger/v1/swagger.json");
                Assert.AreEqual(HttpStatusCode.OK, swagger.StatusCode);

                var rejected = await RunDeployHelperLeaseAsync(sources, fixture.Journal.DatabasePath);
                Assert.AreNotEqual(0, rejected.ExitCode,
                    "The actual deploy helper must reject the same source lock inode while the .NET API owns it.");
                StringAssert.Contains(rejected.Output, "held by another process");
            }
            finally
            {
                await RuntimeFixture.StopAsync(api);
            }
        }
        foreach (var source in sources)
            sourceHashes[source] = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(source)));

        var controlPaths = sources.Select(HorseIdentityMigrationMaintenanceOwnership.GetControlLockPath).ToArray();
        Assert.IsTrue(controlPaths.All(path => !File.Exists(path)));
        var missingControl = await RunDeployHelperLeaseAsync(sources, fixture.Journal.DatabasePath);
        Assert.AreNotEqual(0, missingControl.ExitCode);
        StringAssert.Contains(missingControl.Output, "Supervisor control lock is not a regular unaliased file.");
        Assert.IsTrue(controlPaths.All(path => !File.Exists(path)),
            "The helper must not create missing supervisor control locks.");
        using (var controlLease = HorseIdentityMigrationMaintenanceOwnership.AcquireSupervisorControlLease(sources))
        {
            _ = controlLease.IssueApiBootstrapAuthorization();
            var heldControl = await RunDeployHelperLeaseAsync(sources, fixture.Journal.DatabasePath);
            Assert.AreNotEqual(0, heldControl.ExitCode);
            StringAssert.Contains(heldControl.Output, "An active supervisor control owner exists.");
        }

        var journalLockPath = fixture.Journal.LockPath;
        Assert.IsTrue(File.Exists(journalLockPath));
        File.Delete(journalLockPath);
        var missingJournalLock = await RunDeployHelperLeaseAsync(sources, fixture.Journal.DatabasePath);
        Assert.AreNotEqual(0, missingJournalLock.ExitCode);
        Assert.IsFalse(File.Exists(journalLockPath), "The helper must not recreate a missing journal operation lock.");
        Assert.AreEqual(HorseIdentityMigrationStartupState.Ready,
            (await fixture.Journal.InspectStartupGuardAsync(DateTimeOffset.UtcNow)).State,
            "The existing journal API must restore its own operation lock before the positive interop case.");

        using var lease = StartDeployHelperLease(sources, fixture.Journal.DatabasePath);
        await lease.WaitUntilReadyAsync();
        Assert.AreEqual(HorseIdentityMigrationStartupState.Ready,
            (await fixture.Journal.InspectStartupGuardAsync(DateTimeOffset.UtcNow)).State,
            "The helper-held source/control lease must not itself hold the journal operation lock.");
        using var blockedApi = fixture.StartApiProcess(maintenanceMode: true);
        try
        {
            using var blockedClient = await fixture.WaitForHealthAsync(blockedApi);
            blockedClient.DefaultRequestHeaders.Add("X-Test-Horse-Key", fixture.ApiKey);
            using var blockedStatus = await blockedClient.GetAsync(
                "/api/v2/admin/collection/operations/runtime-status");
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, blockedStatus.StatusCode,
                "The helper-held lease must block the real API without bootstrapping configured stores.");
            foreach (var source in sources)
                Assert.AreEqual(sourceHashes[source],
                    Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(source))),
                    "A blocked API must not mutate any configured source store.");
        }
        finally
        {
            await RuntimeFixture.StopAsync(blockedApi);
            await lease.ReleaseAsync();
        }

        using var resumedApi = fixture.StartApiProcess(maintenanceMode: true);
        try
        {
            using var resumedClient = await fixture.WaitForHealthAsync(resumedApi);
            using var swagger = await resumedClient.GetAsync("/swagger/v1/swagger.json");
            Assert.AreEqual(HttpStatusCode.OK, swagger.StatusCode,
                "After the helper releases its lock, a fresh API process must acquire the same source set and start.");
        }
        finally
        {
            await RuntimeFixture.StopAsync(resumedApi);
        }
    }

    private static async Task<DeployHelperLeaseResult> RunDeployHelperLeaseAsync(
        IReadOnlyList<string> sourcePaths, string journalPath)
    {
        using var lease = StartDeployHelperLease(sourcePaths, journalPath, acquireJournalOperationLock: true);
        await lease.WaitForExitAsync();
        return new(lease.ExitCode, lease.Output);
    }

    private static DeployHelperLease StartDeployHelperLease(IReadOnlyList<string> sourcePaths, string journalPath,
        bool acquireJournalOperationLock = false)
    {
        var repositoryRoot = RuntimeFixture.FindRepositoryRoot();
        var helperPath = Path.Combine(repositoryRoot, "deploy", "horse-id-migration-deploy.sh");
        const string script = "source \"$HRP_DEPLOY_HELPER_PATH\"\n" +
            "horse_id_migration_acquire_source_ownership \"$@\" || exit $?\n" +
            "if [[ \"$HRP_TEST_ACQUIRE_JOURNAL_LOCK\" == 1 ]]; then horse_id_migration_acquire_journal_operation_lock \"$HRP_TEST_JOURNAL_PATH\" || exit $?; fi\n" +
            "echo SOURCE_LOCK_READY\n" +
            "IFS= read -r release_signal || true\n" +
            "horse_id_migration_release_source_ownership\n";
        var process = new Process
        {
            StartInfo = new ProcessStartInfo("bash")
            {
                WorkingDirectory = repositoryRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
            EnableRaisingEvents = true,
        };
        process.StartInfo.ArgumentList.Add("--noprofile");
        process.StartInfo.ArgumentList.Add("--norc");
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add(script);
        process.StartInfo.ArgumentList.Add("deploy-helper-lock-test");
        process.StartInfo.Environment["HRP_DEPLOY_HELPER_PATH"] = helperPath;
        process.StartInfo.Environment["HRP_TEST_JOURNAL_PATH"] = journalPath;
        process.StartInfo.Environment["HRP_TEST_ACQUIRE_JOURNAL_LOCK"] = acquireJournalOperationLock ? "1" : "0";
        foreach (var source in sourcePaths.Order(StringComparer.Ordinal))
        {
            process.StartInfo.ArgumentList.Add(source);
            process.StartInfo.ArgumentList.Add(source);
        }
        var lease = new DeployHelperLease(process);
        lease.Start();
        return lease;
    }

    private sealed record DeployHelperLeaseResult(int ExitCode, string Output);

    private sealed class DeployHelperLease : IDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _output = new();
        private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _outputLock = new();

        internal DeployHelperLease(Process process)
        {
            _process = process;
            _process.OutputDataReceived += (_, eventArgs) => Capture(eventArgs.Data);
            _process.ErrorDataReceived += (_, eventArgs) => Capture(eventArgs.Data);
        }

        internal string Output { get { lock (_outputLock) return _output.ToString(); } }
        internal int ExitCode => _process.ExitCode;

        internal void Start()
        {
            if (!_process.Start()) throw new InvalidOperationException("Deploy helper lease child did not start.");
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        internal async Task WaitUntilReadyAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _ready.Task.WaitAsync(timeout.Token);
        }

        internal async Task WaitForExitAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _process.WaitForExitAsync(timeout.Token);
        }

        internal async Task ReleaseAsync()
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.WriteLine("release");
                _process.StandardInput.Close();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _process.WaitForExitAsync(timeout.Token);
            }
        }

        private void Capture(string? line)
        {
            if (line is null) return;
            lock (_outputLock)
            {
                if (_output.Length < 4096) _output.AppendLine(line);
            }
            if (line == "SOURCE_LOCK_READY") _ready.TrySetResult(true);
        }

        public void Dispose()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(10000);
            }
            _process.Dispose();
        }
    }

    [TestMethod]
    public async Task NormalHostResumesAfterIsolatedScheduleOwnershipIsReleased()
    {
        using var fixture = new RuntimeFixture();
        using (var heldScheduleOwnership = HorseIdentityMigrationRuntimeOwnership.Acquire(
                   [fixture.ScheduleStorePath]))
        {
            using var blockedChild = fixture.StartApiProcess(maintenanceMode: true);
            try
            {
                using var blockedClient = await fixture.WaitForHealthAsync(blockedChild);
                blockedClient.DefaultRequestHeaders.Add("X-Test-Horse-Key", fixture.ApiKey);
                using var blockedRuntime = await blockedClient.GetAsync("/api/v2/admin/collection/operations/runtime-status");
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, blockedRuntime.StatusCode,
                    "A held schedule source must keep the normal host on the journal-only blocked surface.");
                Assert.IsFalse(File.Exists(fixture.EventStorePath));
                Assert.IsFalse(File.Exists(fixture.CollectionStorePath));
                Assert.IsFalse(File.Exists(fixture.ScheduleStorePath));
                Assert.IsFalse(File.Exists(fixture.QueueStorePath));
            }
            finally
            {
                await RuntimeFixture.StopAsync(blockedChild);
            }
        }

        using var normalChild = fixture.StartApiProcess(maintenanceMode: true);
        try
        {
            using var normalClient = await fixture.WaitForHealthAsync(normalChild);
            using var swagger = await normalClient.GetAsync("/swagger/v1/swagger.json");
            Assert.AreEqual(HttpStatusCode.OK, swagger.StatusCode,
                "A fresh host must resume normal startup after the isolated schedule ownership is released.");
        }
        finally
        {
            await RuntimeFixture.StopAsync(normalChild);
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
    public async Task NormalHostServesSwaggerDocumentAfterLegacyJournalUpgrade()
    {
        using var fixture = new RuntimeFixture();
        var operationId = Guid.NewGuid();
        Assert.IsTrue((await fixture.Journal.StageAsync(operationId, Manifest(), DateTimeOffset.UtcNow)).Succeeded);
        await using (var connection = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE migration_request_keys; DROP TABLE migration_execution_intents; DROP TABLE migration_pending_seals; CREATE TABLE migration_request_keys (request_key TEXT PRIMARY KEY NOT NULL, immutable_request_hash TEXT NOT NULL, operation_id TEXT NOT NULL UNIQUE, FOREIGN KEY (operation_id) REFERENCES migration_operations(operation_id) ON DELETE CASCADE); INSERT INTO migration_request_keys (request_key, immutable_request_hash, operation_id) VALUES ($key, $hash, $id);";
            command.Parameters.AddWithValue("$key", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$hash", new string('E', 64));
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        using var child = fixture.StartApiProcess(maintenanceMode: true);
        try
        {
            using var client = await fixture.WaitForHealthAsync(child);
            using var swagger = await client.GetAsync("/swagger/v1/swagger.json");
            Assert.AreEqual(HttpStatusCode.OK, swagger.StatusCode,
                "A valid legacy journal must be upgraded before the normal host serves its public contract.");
            StringAssert.Contains(swagger.Content.Headers.ContentType?.MediaType ?? string.Empty, "json");
        }
        finally
        {
            await RuntimeFixture.StopAsync(child);
        }
    }

    [TestMethod]
    public async Task NormalHostServesSwaggerAfterEmptyPreAdmissionJournalUpgrade()
    {
        using var fixture = new RuntimeFixture();
        await HistoricalHorseIdentityMigrationJournalFixture.CreateAsync(fixture.Journal.DatabasePath,
            fixture.Journal.InitializationMarkerPath, fixture.Journal.LockPath);

        using var child = fixture.StartApiProcess(maintenanceMode: true);
        try
        {
            using var client = await fixture.WaitForHealthAsync(child);
            using var swagger = await client.GetAsync("/swagger/v1/swagger.json");
            Assert.AreEqual(HttpStatusCode.OK, swagger.StatusCode,
                "The host must complete the canonical pre-admission journal upgrade before serving its contract.");
            StringAssert.Contains(swagger.Content.Headers.ContentType?.MediaType ?? string.Empty, "json");
        }
        finally
        {
            await RuntimeFixture.StopAsync(child);
        }

        await using var inspect = new SqliteConnection($"Data Source={fixture.Journal.DatabasePath};Pooling=False");
        await inspect.OpenAsync();
        Assert.AreEqual(0L, await JournalScalarAsync(inspect, "SELECT COUNT(*) FROM migration_operations;"));
        Assert.AreEqual(0L, await JournalScalarAsync(inspect, "SELECT COUNT(*) FROM migration_request_keys;"));
        Assert.AreEqual(0L, await JournalScalarAsync(inspect, "SELECT COUNT(*) FROM migration_execution_intents;"));
        Assert.AreEqual(0L, await JournalScalarAsync(inspect, "SELECT COUNT(*) FROM migration_pending_seals;"));
    }

    private static async Task<long> JournalScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    [TestMethod]
    public async Task StandaloneFixedCommandRejectsPendingRequestWithoutClaimOrIntent()
    {
        await using var stores = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        var sourcePaths = new[] { stores.EventStorePath, stores.CollectionPath, stores.SchedulePath };
        var journal = new HorseIdentityMigrationJournal(Path.GetDirectoryName(stores.CollectionPath)!, sourcePaths);
        var manifest = Manifest();
        var operationId = Guid.NewGuid();
        var requestHash = HorseIdentityMigrationExecutionPolicy.ComputePendingImmutableRequestHash(manifest);
        Assert.IsTrue((await journal.StagePendingWithRequestKeyAsync(operationId, manifest, Guid.NewGuid(), requestHash,
            DateTimeOffset.UtcNow)).Succeeded);
        var before = await stores.SourceSnapshotAsync();

        var result = await RunMigrationCommandAsync(stores,
            [HorseIdentityMigrationRuntimeMode.CommandName, operationId.ToString("D")]);

        Assert.AreEqual(4, result.ExitCode,
            "The standalone legacy command must not claim or fingerprint-bind a pending-only request.");
        Assert.IsNull(await journal.GetExecutionIntentAsync(operationId));
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.Staged,
            (await journal.GetStatusAsync(operationId))!.Status);
        Assert.AreEqual(before, await stores.SourceSnapshotAsync());
        Assert.IsNull(await journal.GetSavedRequestAsync(operationId));
    }

    [TestMethod]
    public Task PendingPrivateChildRejectsMissingBackupBeforeClaim() =>
        AssertPendingPrivateChildRejectsBackupAsync(createBackup: false);

    [TestMethod]
    public Task PendingPrivateChildRejectsBackupManifestMismatchBeforeClaim() =>
        AssertPendingPrivateChildRejectsBackupAsync(createBackup: true);

    [TestMethod]
    public async Task PendingPrivateChildWithValidatedBackupExecutesAndPassesReadonlyPostcheckOnLinux()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Inconclusive("Private-child parent-death authorization requires Linux prctl support.");

        await using var stores = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        var manifest = Manifest();
        var mapping = HorseIdentityMigrationManifestPolicy.Validate(manifest).Mappings.Single();
        const string raceId = "202609190101";
        const string ticketId = "predictionticket-00000000-0000-4000-8000-000000000201";
        await stores.SeedHorseAsync(mapping.OldHorseId);
        await stores.SeedSerializedHorseEventAsync(mapping.OldHorseId);
        await stores.SeedJraResourceAsync(mapping.OldHorseId);
        var oldEntryId = DeterministicIdGenerator.BuildRaceEntryId(raceId, mapping.OldHorseId);
        await ReplaceSupportedEventsWithCommandBusSeedAsync(stores, mapping.OldHorseId, raceId, ticketId, oldEntryId);

        var sources = new HorseIdentityMigrationConfiguredSources(stores.EventStorePath, stores.CollectionPath,
            stores.SchedulePath, LocalCollectionQueuePath: null);
        var journal = new HorseIdentityMigrationJournal(Path.GetDirectoryName(stores.CollectionPath)!,
            sources.PhysicalSourcePaths);
        var backup = HorseIdentityMigrationManagedBackup.ForJournal(journal);
        var operationId = Guid.NewGuid();
        var requestHash = HorseIdentityMigrationExecutionPolicy.ComputePendingImmutableRequestHash(manifest);
        Assert.IsTrue((await journal.StagePendingWithRequestKeyAsync(operationId, manifest, Guid.NewGuid(), requestHash,
            DateTimeOffset.UtcNow)).Succeeded);
        Assert.IsTrue((await journal.CreateExecutionIntentAsync(operationId, requestHash, DateTimeOffset.UtcNow)).Succeeded);
        foreach (var phase in new[]
        {
            HorseIdentityMigrationExecutionPhase.Preparing,
            HorseIdentityMigrationExecutionPhase.Draining,
            HorseIdentityMigrationExecutionPhase.StoppingApi,
        })
            Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId, phase, DateTimeOffset.UtcNow)).Succeeded);

        using var controlLease = HorseIdentityMigrationMaintenanceOwnership.AcquireSupervisorControlLease(
            sources.PhysicalSourcePaths);
        var bootstrap = controlLease.IssueApiBootstrapAuthorization();
        using (var sourceOwnership = HorseIdentityMigrationRuntimeOwnership.Acquire(sources.PhysicalSourcePaths))
            await HorseIdentityMigrationMaintenanceOwnership.InstallMaintenanceGatesAsync(sourceOwnership, bootstrap,
                operationId, requestHash, journal);

        Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId,
            HorseIdentityMigrationExecutionPhase.ApiExited, DateTimeOffset.UtcNow)).Succeeded);
        var receipt = await backup.CreateAsync(operationId, sources, journal);
        Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId,
            HorseIdentityMigrationExecutionPhase.BackedUp, DateTimeOffset.UtcNow)).Succeeded);
        var engine = stores.CreateEngineWithEventFlow(queueProvider: "Sqs");
        var preview = await engine.InspectPreviewAsync(manifest, CancellationToken.None);
        Assert.IsTrue(preview.CanStage);
        Assert.IsNotNull(preview.Fingerprint);
        var sourceSetHash = HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(sources.PhysicalSourcePaths);
        Assert.IsTrue((await journal.SealPendingRequestAsync(operationId, requestHash, preview.Fingerprint!,
            sourceSetHash, receipt.ManifestHash, DateTimeOffset.UtcNow)).Succeeded);
        Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId,
            HorseIdentityMigrationExecutionPhase.Applying, DateTimeOffset.UtcNow)).Succeeded);
        var authorization = await controlLease.IssueOperationAuthorizationAsync(operationId, requestHash, journal);

        var child = await RunMigrationCommandAsync(stores,
            [HorseIdentityMigrationRuntimeMode.CommandName, operationId.ToString("D")],
            privateAuthorization: authorization.SerializeForPrivateEnvironment());

        Assert.AreEqual(0, child.ExitCode,
            $"A validated private child must execute the sealed operation; output: {child.Output}");
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.Succeeded,
            (await journal.GetStatusAsync(operationId))!.Status);
        await using (var eventStore = new SqliteConnection(
                         $"Data Source={stores.EventStorePath};Mode=ReadOnly;Pooling=False"))
        {
            await eventStore.OpenAsync();
            await using var command = eventStore.CreateCommand();
            command.CommandText = "SELECT HorseId FROM Horses;";
            Assert.AreEqual(mapping.DestinationHorseId, await command.ExecuteScalarAsync());
        }
        var verifier = new HorseIdentityMigrationPostVerification(backup,
            stores.EventFlowServices.GetRequiredService<EventFlow.EventStores.IEventJsonSerializer>(),
            stores.EventFlowServices.GetRequiredService<EventFlow.EventStores.IDomainEventFactory>());
        await verifier.VerifyAsync(operationId, sources, journal,
            HorseIdentityMigrationManifestPolicy.Validate(manifest).Mappings, CancellationToken.None);
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
        TimeSpan? timeout = null, bool configureIsolatedAws = false, string? privateAuthorization = null)
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
        if (privateAuthorization is not null)
            process.StartInfo.Environment[HorseIdentityMigrationMaintenanceCapability.PrivateEnvironmentVariable] =
                privateAuthorization;
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

    private static async Task AssertPendingPrivateChildRejectsBackupAsync(bool createBackup)
    {
        if (!OperatingSystem.IsLinux())
            Assert.Inconclusive("Private-child parent-death authorization requires Linux prctl support.");

        await using var stores = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        var sources = new HorseIdentityMigrationConfiguredSources(stores.EventStorePath, stores.CollectionPath,
            stores.SchedulePath, LocalCollectionQueuePath: null);
        var journal = new HorseIdentityMigrationJournal(Path.GetDirectoryName(stores.CollectionPath)!,
            sources.PhysicalSourcePaths);
        var manifest = Manifest();
        var operationId = Guid.NewGuid();
        var requestHash = HorseIdentityMigrationExecutionPolicy.ComputePendingImmutableRequestHash(manifest);
        Assert.IsTrue((await journal.StagePendingWithRequestKeyAsync(operationId, manifest, Guid.NewGuid(), requestHash,
            DateTimeOffset.UtcNow)).Succeeded);
        Assert.IsTrue((await journal.CreateExecutionIntentAsync(operationId, requestHash, DateTimeOffset.UtcNow)).Succeeded);
        foreach (var phase in new[]
        {
            HorseIdentityMigrationExecutionPhase.Preparing,
            HorseIdentityMigrationExecutionPhase.Draining,
            HorseIdentityMigrationExecutionPhase.StoppingApi,
        })
            Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId, phase, DateTimeOffset.UtcNow)).Succeeded);

        using var controlLease = HorseIdentityMigrationMaintenanceOwnership.AcquireSupervisorControlLease(
            sources.PhysicalSourcePaths);
        var bootstrap = controlLease.IssueApiBootstrapAuthorization();
        using (var sourceOwnership = HorseIdentityMigrationRuntimeOwnership.Acquire(sources.PhysicalSourcePaths))
            await HorseIdentityMigrationMaintenanceOwnership.InstallMaintenanceGatesAsync(sourceOwnership, bootstrap,
                operationId, requestHash, journal);

        Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId,
            HorseIdentityMigrationExecutionPhase.ApiExited, DateTimeOffset.UtcNow)).Succeeded);
        Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId,
            HorseIdentityMigrationExecutionPhase.BackedUp, DateTimeOffset.UtcNow)).Succeeded);

        string? actualBackupManifestHash = null;
        if (createBackup)
        {
            var receipt = await HorseIdentityMigrationManagedBackup.ForJournal(journal)
                .CreateAsync(operationId, sources, journal);
            actualBackupManifestHash = receipt.ManifestHash;
        }
        var sealedBackupManifestHash = new string('D', 64);
        Assert.IsFalse(string.Equals(actualBackupManifestHash, sealedBackupManifestHash, StringComparison.Ordinal));
        Assert.IsTrue((await journal.SealPendingRequestAsync(operationId, requestHash, new string('A', 64),
            HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash(sources.PhysicalSourcePaths),
            sealedBackupManifestHash, DateTimeOffset.UtcNow)).Succeeded);
        Assert.IsTrue((await journal.AdvanceExecutionIntentAsync(operationId,
            HorseIdentityMigrationExecutionPhase.Applying, DateTimeOffset.UtcNow)).Succeeded);
        var authorization = await controlLease.IssueOperationAuthorizationAsync(operationId, requestHash, journal);

        var result = await RunMigrationCommandAsync(stores,
            [HorseIdentityMigrationRuntimeMode.CommandName, operationId.ToString("D")],
            privateAuthorization: authorization.SerializeForPrivateEnvironment());

        Assert.AreEqual(createBackup ? 4 : 5, result.ExitCode,
            "A receipt-manifest conflict returns 4 after valid private authorization; a missing receipt fails validation with 5.");
        Assert.AreEqual(HorseIdentityMigrationOperationStatus.Staged, (await journal.GetStatusAsync(operationId))!.Status);
        Assert.AreEqual(HorseIdentityMigrationExecutionPhase.Applying,
            (await journal.GetExecutionIntentAsync(operationId))!.Phase,
            "The supervisor's seal phase remains Applying, and the child must not claim the operation.");
        Assert.IsNull(await journal.GetClaimedStagedRequestAsync(operationId));
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
        internal string Root => _root;
        internal string EventStorePath { get; }
        internal string CollectionStorePath { get; }
        internal string ScheduleStorePath { get; }
        internal string QueueStorePath { get; }
        internal string ApiKey { get; }
        internal string OperatorUrl => $"http://127.0.0.1:{_port}";
        internal string OperatorUrls => $"http://+:{_port}";
        internal HorseIdentityMigrationJournal Journal { get; }

        internal Process StartApiProcess(bool maintenanceMode = false, bool operatorOnly = false,
            string? operatorOperationId = null)
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
            if (operatorOnly)
                process.StartInfo.Environment[HorseIdentityMigrationRuntimeMode.OperatorOnlyEnvironmentVariable] = "true";
            if (operatorOperationId is not null)
                process.StartInfo.Environment[HorseIdentityMigrationRuntimeMode.OperatorOperationIdEnvironmentVariable] = operatorOperationId;
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

        internal static string FindRepositoryRoot()
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

    private sealed class OperatorTestEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues = new(StringComparer.Ordinal);

        internal OperatorTestEnvironment(RuntimeFixture fixture)
        {
            Set("CollectionPlatform__StateDirectory", fixture.StateDirectory);
            Set("ConnectionStrings__EventStore", $"Data Source={fixture.EventStorePath}");
            Set("PredictionScheduling__StateDirectory", Path.GetDirectoryName(fixture.ScheduleStorePath)!);
            Set("PredictionScheduling__StoreFileName", Path.GetFileName(fixture.ScheduleStorePath));
            Set("CollectionQueue__Provider", "Local");
            Set("CollectionQueue__LocalDatabasePath", fixture.QueueStorePath);
            Set("ApiKey__HeaderName", "X-Test-Horse-Key");
            Set("ApiKey__Key", fixture.ApiKey);
        }

        private void Set(string name, string value)
        {
            _originalValues[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            foreach (var pair in _originalValues)
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
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
