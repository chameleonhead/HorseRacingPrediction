using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventFlow.EntityFramework;
using HorseRacingPrediction.Api;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Api.Endpoints.Identity;
using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web.ApiBrowsing;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Horses;
using HorseRacingPrediction.Contracts.Identity;
using HorseRacingPrediction.Infrastructure.Persistence;
using HorseRacingPrediction.PredictionScheduling;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class IdentityMigrationPreflightEndpointsTests
{
    [TestMethod]
    public async Task Get_UsesAdminApiKeyRoute_NoStoreAndLeavesEventStoreUnchanged()
    {
        var root = Path.Combine(Path.GetTempPath(), "identity-migration-preflight-endpoint", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventPath = Path.Combine(root, "events.db");
        var (fixtureApp, fixtureClient) = await TestApplicationFactory.CreateAsync($"Data Source={eventPath};Pooling=False");
        fixtureClient.Dispose();
        try
        {
            using var serviceProvider = CreateServiceProvider(fixtureApp);
            var service = serviceProvider.GetRequiredService<IdentityMigrationPreflightService>();
            var app = await StartApiAsync(service);
            try
            {
                using var client = app.GetTestClient();

                using (var unauthenticated = await client.GetAsync("/api/v2/admin/identity-migration-preflight"))
                    Assert.AreEqual(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

                client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
                var before = await SnapshotAsync(eventPath);
                using var response = await client.GetAsync("/api/v2/admin/identity-migration-preflight");
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                Assert.AreEqual("no-store", response.Headers.CacheControl?.ToString());
                var body = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<IdentityMigrationPreflightResponse>(body,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                Assert.IsNotNull(result);
                Assert.AreEqual(IdentityMigrationPreflightPhase.HorseRoots, result.Page.Phase);
                Assert.IsTrue(result.Page.IsNonAtomic);
                Assert.IsNotNull(result.Page.NextCursor);
                Assert.IsFalse(body.Contains("sourceUrl", StringComparison.OrdinalIgnoreCase));
                Assert.IsFalse(body.Contains("Metadata", StringComparison.OrdinalIgnoreCase));
                CollectionAssert.AreEqual(before, await SnapshotAsync(eventPath));
            }
            finally
            {
                await app.DisposeAsync();
            }
        }
        finally
        {
            await fixtureApp.DisposeAsync();
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    [TestMethod]
    public async Task AdminClient_RoundTripsThroughServiceBackedHttpEndpointAndPreservesEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "identity-migration-preflight-client-endpoint", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventPath = Path.Combine(root, "events.db");
        var (fixtureApp, fixtureClient) = await TestApplicationFactory.CreateAsync($"Data Source={eventPath};Pooling=False");
        using (fixtureClient)
        {
            try
            {
                fixtureClient.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
                var horseId = $"horse-{Guid.NewGuid():D}";
                const string registeredName = "Preflight transport horse";
                const string normalizedName = "preflight-transport-horse";
                var birthDate = new DateOnly(2020, 4, 5);
                using var registration = await fixtureClient.PostAsJsonAsync("/api/horses",
                    new RegisterHorseRequest(new(registeredName, normalizedName, "牡", birthDate, horseId)));
                Assert.AreEqual(HttpStatusCode.Created, registration.StatusCode);

                var collectionStore = new CollectionPlatformStore(
                    fixtureApp.Services.GetRequiredService<IOptions<CollectionPlatformOptions>>());
                var definition = new CollectionDefinitionId("preflight-client-horse-profile");
                await collectionStore.RegisterDefinitionAsync(definition, "Horse profile",
                    CollectionResourceType.Horse, 1, "preflight", false);
                await collectionStore.RequestAsync(new(CollectionResourceType.Horse, "JRA", horseId),
                    definition, 1, CollectionReason.Initial, DateTimeOffset.UtcNow,
                    attributes: new Dictionary<string, string>
                    {
                        ["sourceIdentity"] = "jra-horse:1234567890",
                        ["sourceUrl"] = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002024123456/FC",
                    });

                var raceId = $"race-{Guid.NewGuid():D}";
                await SeedSourceAndReferenceAsync(eventPath, horseId, raceId);
                using var services = CreateServiceProvider(fixtureApp);
                var api = await StartApiAsync(services.GetRequiredService<IdentityMigrationPreflightService>());
                try
                {
                    using var transport = api.GetTestClient();
                    var adminClient = new AdminApiClient(transport,
                        new AdminApiBaseAddressResolver(api.Services.GetRequiredService<IServer>()),
                        Options.Create(new ApiKeyOptions
                        {
                            HeaderName = "X-Api-Key",
                            Key = TestApplicationFactory.TestApiKey,
                        }));

                    var page = await adminClient.GetIdentityMigrationPreflightAsync(null);
                    Assert.IsNotNull(page);
                    Assert.AreEqual(IdentityMigrationPreflightPhase.HorseRoots, page.Phase);
                    for (var phaseIndex = 0;
                         phaseIndex < (int)IdentityMigrationPreflightPhase.HorseRaceHistories; phaseIndex++)
                    {
                        Assert.AreEqual(IdentityMigrationPreflightPageState.Incomplete, page.State);
                        Assert.IsNotNull(page.NextCursor);
                        page = await adminClient.GetIdentityMigrationPreflightAsync(page.NextCursor);
                        Assert.IsNotNull(page);
                    }
                    Assert.AreEqual(IdentityMigrationPreflightPageState.Incomplete, page.State);
                    Assert.AreEqual(IdentityMigrationPreflightPhase.HorseRaceHistories, page.Phase);
                    Assert.IsNotNull(page.ObservedAtUtc);
                    Assert.AreEqual(1, page.RowsInspected);
                    Assert.IsNotNull(page.Counts);
                    Assert.AreEqual(4, page.Surfaces.Count);
                    Assert.IsTrue(page.IsNonAtomic);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(page.NextCursor));

                    var evidence = page.Evidence.Single(item => item.HorseId == horseId);
                    Assert.AreEqual(IdentityMigrationPreflightObservationStatus.Unverifiable, evidence.Status);
                    Assert.AreEqual(registeredName, evidence.CurrentRegisteredName);
                    Assert.AreEqual(normalizedName, evidence.CurrentNormalizedName);
                    Assert.AreEqual(birthDate, evidence.CurrentBirthDate);
                    Assert.AreEqual(registeredName, evidence.InitialRegisteredName);
                    Assert.AreEqual(normalizedName, evidence.InitialNormalizedName);
                    Assert.AreEqual(birthDate, evidence.InitialBirthDate);
                    Assert.AreEqual("jra-horse:1234567890", evidence.CanonicalJraIdentity);
                    Assert.AreEqual(IdentityMigrationPreflightObservationStatus.Observed, evidence.JraIdentityStatus);
                    Assert.AreEqual(1, evidence.RelatedRaceCount);
                    Assert.IsFalse(evidence.RelatedRacesTruncated);
                    Assert.IsTrue(evidence.RelatedRaces.Count <= 16);
                    Assert.AreEqual(0, evidence.RelatedRaces.Count);
                }
                finally
                {
                    await api.DisposeAsync();
                }
            }
            finally
            {
                fixtureClient.Dispose();
                await fixtureApp.DisposeAsync();
                await DeleteDirectoryEventuallyAsync(root);
            }
        }
    }

    [TestMethod]
    public async Task AdminClient_ValidCursorReportsLockedStore503AndRetriesAfterSameStoreReopens()
    {
        var root = Path.Combine(Path.GetTempPath(), "identity-migration-preflight-client-locked-read",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventPath = Path.Combine(root, "events.db");
        var (fixtureApp, fixtureClient) = await TestApplicationFactory.CreateAsync($"Data Source={eventPath};Pooling=False");
        try
        {
            fixtureClient.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            var horseId = $"horse-{Guid.NewGuid():D}";
            using (var registration = await fixtureClient.PostAsJsonAsync("/api/horses",
                new RegisterHorseRequest(new($"Preflight lock {Guid.NewGuid():N}", "preflight-lock", "牡",
                    new DateOnly(2020, 4, 5), horseId))))
                Assert.AreEqual(HttpStatusCode.Created, registration.StatusCode);

            _ = new CollectionPlatformStore(fixtureApp.Services.GetRequiredService<IOptions<CollectionPlatformOptions>>());
            using var services = CreateServiceProvider(fixtureApp);
            var service = services.GetRequiredService<IdentityMigrationPreflightService>();
            var api = await StartApiAsync(service);
            try
            {
                using var transport = api.GetTestClient();
                var adminClient = new AdminApiClient(transport,
                    new AdminApiBaseAddressResolver(api.Services.GetRequiredService<IServer>()),
                    Options.Create(new ApiKeyOptions
                    {
                        HeaderName = "X-Api-Key",
                        Key = TestApplicationFactory.TestApiKey,
                    }));

                var first = await adminClient.GetIdentityMigrationPreflightAsync(null);
                Assert.IsNotNull(first?.NextCursor);
                var cursor = first.NextCursor;
                var before = await SnapshotAsync(eventPath);

                await using (var blocker = new SqliteConnection($"Data Source={eventPath};Pooling=False"))
                {
                    await blocker.OpenAsync();
                    await using (var lockCommand = blocker.CreateCommand())
                    {
                        lockCommand.CommandText = "PRAGMA locking_mode=EXCLUSIVE; BEGIN EXCLUSIVE;";
                        await lockCommand.ExecuteNonQueryAsync();
                    }

                    try
                    {
                        var invalid = await Assert.ThrowsAsync<HttpRequestException>(
                            () => adminClient.GetIdentityMigrationPreflightAsync(cursor + "x"));
                        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);

                        var unavailable = await Assert.ThrowsAsync<HttpRequestException>(
                            () => adminClient.GetIdentityMigrationPreflightAsync(cursor));
                        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
                    }
                    finally
                    {
                        await using var unlock = blocker.CreateCommand();
                        unlock.CommandText = "ROLLBACK; PRAGMA locking_mode=NORMAL;";
                        await unlock.ExecuteNonQueryAsync();
                    }
                }

                var retry = await adminClient.GetIdentityMigrationPreflightAsync(cursor);
                Assert.IsNotNull(retry);
                Assert.AreEqual(IdentityMigrationPreflightPhase.HorseEvents, retry.Phase);

                await using (var schemaConnection = new SqliteConnection($"Data Source={eventPath};Pooling=False"))
                {
                    await schemaConnection.OpenAsync();
                    await using (var addUnknown = schemaConnection.CreateCommand())
                    {
                        addUnknown.CommandText = "CREATE TABLE AddedAfterCursor (Value TEXT NOT NULL);";
                        await addUnknown.ExecuteNonQueryAsync();
                    }

                    try
                    {
                        var drift = await Assert.ThrowsAsync<HttpRequestException>(
                            () => adminClient.GetIdentityMigrationPreflightAsync(cursor));
                        Assert.AreEqual(HttpStatusCode.BadRequest, drift.StatusCode);
                    }
                    finally
                    {
                        await using var dropUnknown = schemaConnection.CreateCommand();
                        dropUnknown.CommandText = "DROP TABLE AddedAfterCursor;";
                        await dropUnknown.ExecuteNonQueryAsync();
                    }
                }

                var recovered = await adminClient.GetIdentityMigrationPreflightAsync(cursor);
                Assert.IsNotNull(recovered);
                Assert.AreEqual(IdentityMigrationPreflightPhase.HorseEvents, recovered.Phase);
                CollectionAssert.AreEqual(before, await SnapshotAsync(eventPath));
            }
            finally
            {
                await api.DisposeAsync();
            }
        }
        finally
        {
            fixtureClient.Dispose();
            await fixtureApp.DisposeAsync();
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    [TestMethod]
    public async Task AdminClient_OptionalInitialUnavailableSurfaceReturnsPartialSuccess()
    {
        var root = Path.Combine(Path.GetTempPath(), "identity-migration-preflight-client-optional-unavailable",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventPath = Path.Combine(root, "events.db");
        var scheduleDirectory = Path.Combine(root, "schedule");
        var schedulePath = Path.Combine(scheduleDirectory, "prediction-executions.db");
        var (fixtureApp, fixtureClient) = await TestApplicationFactory.CreateAsync($"Data Source={eventPath};Pooling=False");
        try
        {
            var collectionOptions = fixtureApp.Services.GetRequiredService<IOptions<CollectionPlatformOptions>>();
            _ = new CollectionPlatformStore(collectionOptions);
            Directory.CreateDirectory(scheduleDirectory);
            var eventReader = new IdentityMigrationPreflightEventStoreReader(
                fixtureApp.Services.GetRequiredService<IDbContextProvider<EventStoreDbContext>>());
            var configuredReader = new IdentityMigrationPreflightConfiguredSurfaceReader(
                collectionOptions,
                Options.Create(new PredictionScheduleOptions { StateDirectory = scheduleDirectory }),
                Options.Create(new CollectionQueueOptions { Provider = "Sqs" }),
                fixtureApp.Services.GetRequiredService<IWebHostEnvironment>());
            var protector = new IdentityMigrationPreflightCursorProtector(
                fixtureApp.Services.GetRequiredService<IDataProtectionProvider>(), TimeProvider.System);
            var service = new IdentityMigrationPreflightService(eventReader, configuredReader, protector, TimeProvider.System);
            var api = await StartApiAsync(service);
            try
            {
                using var transport = api.GetTestClient();
                var adminClient = new AdminApiClient(transport,
                    new AdminApiBaseAddressResolver(api.Services.GetRequiredService<IServer>()),
                    Options.Create(new ApiKeyOptions
                    {
                        HeaderName = "X-Api-Key",
                        Key = TestApplicationFactory.TestApiKey,
                    }));

                var page = await adminClient.GetIdentityMigrationPreflightAsync(null);
                Assert.IsNotNull(page);
                Assert.AreEqual(IdentityMigrationPreflightPageState.Incomplete, page.State);
                Assert.AreEqual(IdentityMigrationPreflightObservationStatus.Unavailable,
                    page.Surfaces.Single(item => item.Surface == IdentityMigrationPreflightSurface.PredictionSchedule).Status);
                Assert.IsNotNull(page.NextCursor);
                Assert.IsFalse(File.Exists(schedulePath), "The partial response must not create the optional store.");
            }
            finally
            {
                await api.DisposeAsync();
            }
        }
        finally
        {
            fixtureClient.Dispose();
            await fixtureApp.DisposeAsync();
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    [TestMethod]
    public async Task Get_MapsInvalidCursorBusyAndUnavailableToSanitizedStatuses()
    {
        var root = Path.Combine(Path.GetTempPath(), "identity-migration-preflight-endpoint-errors", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventPath = Path.Combine(root, "events.db");
        var (fixtureApp, fixtureClient) = await TestApplicationFactory.CreateAsync($"Data Source={eventPath};Pooling=False");
        fixtureClient.Dispose();
        try
        {
            using var serviceProvider = CreateServiceProvider(fixtureApp);
            var service = serviceProvider.GetRequiredService<IdentityMigrationPreflightService>();
            var app = await StartApiAsync(service);
            try
            {
                using var client = app.GetTestClient();
                client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);

                using (var invalid = await client.GetAsync($"/api/v2/admin/identity-migration-preflight?cursor={new string('x', 4100)}"))
                {
                    Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
                    Assert.AreEqual("no-store", invalid.Headers.CacheControl?.ToString());
                    Assert.DoesNotContain("xxxx", await invalid.Content.ReadAsStringAsync());
                }

                var admission = (SemaphoreSlim)typeof(IdentityMigrationPreflightService)
                    .GetField("Admission", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                    .GetValue(null)!;
                await admission.WaitAsync();
                try
                {
                    using var busy = await client.GetAsync("/api/v2/admin/identity-migration-preflight");
                    Assert.AreEqual(HttpStatusCode.TooManyRequests, busy.StatusCode);
                    Assert.AreEqual("no-store", busy.Headers.CacheControl?.ToString());
                }
                finally
                {
                    admission.Release();
                }

                using var missingProvider = CreateServiceProvider(fixtureApp,
                    new DeferredEventStoreContextProvider(Path.Combine(root, "never-created.db")));
                var unavailable = missingProvider.GetRequiredService<IdentityMigrationPreflightService>();
                var unavailableApp = await StartApiAsync(unavailable);
                try
                {
                    using var unavailableClient = unavailableApp.GetTestClient();
                    unavailableClient.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
                    using var unavailableResponse = await unavailableClient.GetAsync("/api/v2/admin/identity-migration-preflight");
                    Assert.AreEqual(HttpStatusCode.ServiceUnavailable, unavailableResponse.StatusCode);
                    Assert.AreEqual("no-store", unavailableResponse.Headers.CacheControl?.ToString());
                    var safeBody = await unavailableResponse.Content.ReadAsStringAsync();
                    Assert.DoesNotContain("never-created.db", safeBody);
                    Assert.IsFalse(File.Exists(Path.Combine(root, "never-created.db")));
                }
                finally
                {
                    await unavailableApp.DisposeAsync();
                }
            }
            finally
            {
                await app.DisposeAsync();
            }
        }
        finally
        {
            await fixtureApp.DisposeAsync();
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    private static async Task<WebApplication> StartApiAsync(IdentityMigrationPreflightService service)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.Configure<ApiKeyOptions>(options =>
        {
            options.HeaderName = "X-Api-Key";
            options.Key = TestApplicationFactory.TestApiKey;
        });
        builder.Services.AddSingleton<ApiKeyEndpointFilter>();
        builder.Services.AddSingleton(service);
        var app = builder.Build();
        GetIdentityMigrationPreflightEndpoint.Map(app.MapGroup("/api")
            .AddEndpointFilter<ApiKeyEndpointFilter>());
        await app.StartAsync();
        return app;
    }

    private static ServiceProvider CreateServiceProvider(WebApplication fixture,
        IDbContextProvider<EventStoreDbContext>? eventStoreProvider = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(eventStoreProvider ?? fixture.Services.GetRequiredService<IDbContextProvider<EventStoreDbContext>>());
        services.AddSingleton(fixture.Services.GetRequiredService<IOptions<CollectionPlatformOptions>>());
        services.AddSingleton(fixture.Services.GetRequiredService<IOptions<PredictionScheduleOptions>>());
        services.AddSingleton(fixture.Services.GetRequiredService<IOptions<CollectionQueueOptions>>());
        services.AddSingleton(fixture.Services.GetRequiredService<IWebHostEnvironment>());
        services.AddSingleton(fixture.Services.GetRequiredService<IDataProtectionProvider>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IdentityMigrationPreflightEventStoreReader>();
        services.AddSingleton<IdentityMigrationPreflightConfiguredSurfaceReader>();
        services.AddSingleton(services => new IdentityMigrationPreflightCursorProtector(
            services.GetRequiredService<IDataProtectionProvider>(),
            services.GetRequiredService<TimeProvider>()));
        services.AddSingleton(services => new IdentityMigrationPreflightService(
            services.GetRequiredService<IdentityMigrationPreflightEventStoreReader>(),
            services.GetRequiredService<IdentityMigrationPreflightConfiguredSurfaceReader>(),
            services.GetRequiredService<IdentityMigrationPreflightCursorProtector>(),
            services.GetRequiredService<TimeProvider>()));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<string[]> SnapshotAsync(string path)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ConnectionString);
        await connection.OpenAsync();
        var rows = new List<string>();
        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText = "SELECT type || ':' || name FROM sqlite_master ORDER BY type, name;";
            await using var reader = await schema.ExecuteReaderAsync();
            while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        }
        await using (var events = connection.CreateCommand())
        {
            events.CommandText = "SELECT COUNT(*) FROM EventEntity;";
            rows.Add($"events:{await events.ExecuteScalarAsync()}");
        }
        return rows.ToArray();
    }

    private static async Task SeedSourceAndReferenceAsync(string eventPath, string horseId, string raceId)
    {
        await using var connection = new SqliteConnection($"Data Source={eventPath};Pooling=False");
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var profile = connection.CreateCommand())
        {
            profile.Transaction = (SqliteTransaction)transaction;
            profile.CommandText = """
                INSERT INTO JraSubjectProfileReadModel (SubjectId, Name, SourceIdentity, SourceUrl, AcquiredAt, Fields)
                VALUES ($id, 'profile', 'jra-horse:1234567890', '', $at, '{}');
                """;
            profile.Parameters.AddWithValue("$id", horseId);
            profile.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            await profile.ExecuteNonQueryAsync();
        }
        await using (var history = connection.CreateCommand())
        {
            history.Transaction = (SqliteTransaction)transaction;
            history.CommandText = "INSERT INTO HorseRaceHistories (HorseId, Entries) VALUES ($id, $entries);";
            history.Parameters.AddWithValue("$id", horseId);
            history.Parameters.AddWithValue("$entries", $"[{{\"RaceId\":\"{raceId}\",\"HorseNumber\":9}}]");
            await history.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    private static async Task DeleteDirectoryEventuallyAsync(string path)
    {
        for (var attempt = 0; attempt < 10 && Directory.Exists(path); attempt++)
        {
            try { Directory.Delete(path, recursive: true); }
            catch (IOException) { await Task.Delay(50); }
            catch (UnauthorizedAccessException) { await Task.Delay(50); }
        }
        Assert.IsFalse(Directory.Exists(path), "The isolated test fixture should be removable after disposal.");
    }

    private sealed class DeferredEventStoreContextProvider(string path) : IDbContextProvider<EventStoreDbContext>
    {
        private readonly DbContextOptions<EventStoreDbContext> _options = new DbContextOptionsBuilder<EventStoreDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False").Options;

        public EventStoreDbContext CreateContext() => new(_options);
    }
}
