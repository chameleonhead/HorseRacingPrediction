using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using HorseRacingPrediction.Api;
using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Horses;
using HorseRacingPrediction.Contracts.Identity;
using HorseRacingPrediction.Infrastructure.Persistence;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.PredictionScheduling;
using EventFlow.EntityFramework;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class IdentityMigrationPreflightServiceTests
{
    [TestMethod]
    public async Task OversizedCursorIsRejectedBeforeAnyStoreAccess()
    {
        var service = new IdentityMigrationPreflightService(null!, null!, null!, TimeProvider.System);

        await Assert.ThrowsAsync<IdentityMigrationPreflightInvalidCursorException>(
            () => service.GetPageAsync(new string('x', IdentityMigrationPreflightPolicy.MaximumCursorCharacters + 1),
                CancellationToken.None));
    }

    [TestMethod]
    public async Task RequiredEventStoreUnavailableIsNotReturnedAsAnIncompletePage()
    {
        var root = Path.Combine(Path.GetTempPath(), "identity-migration-preflight-required-unavailable",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var missingPath = Path.Combine(root, "not-created.db");
        try
        {
            var (app, client) = await TestApplicationFactory.CreateAsync(
                $"Data Source={Path.Combine(root, "host-events.db")};Pooling=False");
            await using var application = app;
            using var http = client;
            var reader = new IdentityMigrationPreflightEventStoreReader(
                new DeferredEventStoreContextProvider(missingPath));
            var configured = new IdentityMigrationPreflightConfiguredSurfaceReader(
                Options.Create(new CollectionPlatformOptions { StateDirectory = Path.Combine(root, "collection") }),
                Options.Create(new PredictionScheduleOptions { StateDirectory = Path.Combine(root, "schedule") }),
                Options.Create(new CollectionQueueOptions { Provider = "Sqs" }),
                app.Services.GetRequiredService<IWebHostEnvironment>());
            var provider = app.Services.GetRequiredService<IDataProtectionProvider>();
            var service = new IdentityMigrationPreflightService(reader, configured,
                new IdentityMigrationPreflightCursorProtector(provider, TimeProvider.System), TimeProvider.System);

            await Assert.ThrowsAsync<IdentityMigrationPreflightUnavailableException>(
                () => service.GetPageAsync(null, CancellationToken.None));
            Assert.IsFalse(File.Exists(missingPath), "A missing configured database must not be created by inspection.");
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    [TestMethod]
    public async Task FirstPage_ActivatesThroughDefaultDiAndReadsWithoutChangingDatabaseRowsOrSchema()
    {
        var root = Path.Combine(Path.GetTempPath(), "identity-migration-preflight", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventStorePath = Path.Combine(root, "events.db");
        try
        {
            var (app, client) = await TestApplicationFactory.CreateAsync($"Data Source={eventStorePath};Pooling=False");
            await using var application = app;
            using var http = client;
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);

            var horseId = $"horse-{Guid.NewGuid():D}";
            var registeredName = $"Preflight {Guid.NewGuid():N}";
            using var registration = await http.PostAsJsonAsync("/api/horses",
                new RegisterHorseRequest(new(registeredName, "preflight-normalized", "牡",
                    new DateOnly(2021, 2, 3), horseId)));
            Assert.AreEqual(HttpStatusCode.Created, registration.StatusCode);

            var collectionStore = app.Services.GetRequiredService<CollectionPlatformStore>();
            await collectionStore.RegisterDefinitionAsync(new("preflight-horse-profile"), "Horse profile",
                CollectionResourceType.Horse, 1, "preflight", false);
            await collectionStore.RequestAsync(new(CollectionResourceType.Horse, "JRA", horseId),
                new("preflight-horse-profile"), 1, CollectionReason.Initial, DateTimeOffset.UtcNow,
                attributes: new Dictionary<string, string>
                {
                    ["sourceIdentity"] = "jra-horse:1234567890",
                    ["sourceUrl"] = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002024123456/FC",
                });

            var collectionStorePath = Path.Combine(Path.GetFullPath(eventStorePath) + ".collection",
                "collection-platform.db");
            var before = await SnapshotAsync([eventStorePath, collectionStorePath]);

            using var serviceProvider = CreatePreflightServiceProvider(app);
            var service = serviceProvider.GetRequiredService<IdentityMigrationPreflightService>();
            var page = await service.GetPageAsync(null, CancellationToken.None);

            Assert.AreNotEqual(IdentityMigrationPreflightPageState.Complete, page.State);
            Assert.AreEqual(IdentityMigrationPreflightPhase.HorseRoots, page.Phase);
            Assert.IsTrue(page.IsNonAtomic);
            Assert.AreEqual(4, page.Surfaces.Count);
            Assert.AreEqual(IdentityMigrationPreflightObservationStatus.NotConfigured,
                page.Surfaces.Single(item => item.Surface == IdentityMigrationPreflightSurface.LocalCollectionQueue).Status);
            Assert.IsTrue(page.Evidence.Any(item => item.HorseId == horseId
                && item.CurrentRegisteredName == registeredName
                && item.InitialRegisteredName == registeredName));
            Assert.IsTrue(JsonSerializer.SerializeToUtf8Bytes(page).Length <= 1024 * 1024);
            Assert.IsFalse(string.IsNullOrWhiteSpace(page.NextCursor),
                $"state={page.State}; surfaces={string.Join(',', page.Surfaces.Select(item => $"{item.Surface}:{item.Status}"))}; counts={page.Counts}");
            CollectionAssert.AreEqual(before, await SnapshotAsync([eventStorePath, collectionStorePath]));

            await using var readOnly = await OpenReadOnlyAsync(eventStorePath);
            await using var deniedWrite = readOnly.CreateCommand();
            deniedWrite.CommandText = "UPDATE Horses SET RegisteredName = RegisteredName WHERE HorseId = $id;";
            deniedWrite.Parameters.AddWithValue("$id", horseId);
            await Assert.ThrowsAsync<SqliteException>(() => deniedWrite.ExecuteNonQueryAsync());
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    [TestMethod]
    public async Task CursorAdvancesThroughSixFixedPhasesAndRejectsTampering()
    {
        var root = Path.Combine(Path.GetTempPath(), "identity-migration-preflight-pages", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventStorePath = Path.Combine(root, "events.db");
        try
        {
            var (app, client) = await TestApplicationFactory.CreateAsync($"Data Source={eventStorePath};Pooling=False");
            await using var application = app;
            using var http = client;
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);

            _ = new CollectionPlatformStore(app.Services.GetRequiredService<IOptions<CollectionPlatformOptions>>());

            using var serviceProvider = CreatePreflightServiceProvider(app);
            var service = serviceProvider.GetRequiredService<IdentityMigrationPreflightService>();
            var phases = new List<IdentityMigrationPreflightPhase>();
            IdentityMigrationPreflightPage? page = await service.GetPageAsync(null, CancellationToken.None);
            while (page is not null)
            {
                phases.Add(page.Phase);
                if (page.State == IdentityMigrationPreflightPageState.Complete) break;
                if (page.NextCursor is null) break;
                page = await service.GetPageAsync(page.NextCursor, CancellationToken.None);
            }

            CollectionAssert.AreEqual(Enum.GetValues<IdentityMigrationPreflightPhase>(), phases.ToArray());
            Assert.AreEqual(IdentityMigrationPreflightPhase.CollectionResources, page!.Phase);

            var first = await service.GetPageAsync(null, CancellationToken.None);
            Assert.IsNotNull(first.NextCursor);
            var modifiedCursor = first.NextCursor![..^1] + (first.NextCursor[^1] == 'A' ? "B" : "A");
            await Assert.ThrowsAsync<IdentityMigrationPreflightInvalidCursorException>(
                () => service.GetPageAsync(modifiedCursor, CancellationToken.None));
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    [TestMethod]
    public async Task RootPhaseUses128KeyPagesAndReachesThe129thRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "identity-migration-preflight-pagination", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventStorePath = Path.Combine(root, "events.db");
        try
        {
            var (app, client) = await TestApplicationFactory.CreateAsync($"Data Source={eventStorePath};Pooling=False");
            await using var application = app;
            using var http = client;
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            var registeredId = await RegisterFixtureHorseAsync(http);
            await SeedHorseRowsAsync(eventStorePath, 128);
            _ = new CollectionPlatformStore(app.Services.GetRequiredService<IOptions<CollectionPlatformOptions>>());

            using var serviceProvider = CreatePreflightServiceProvider(app);
            var service = serviceProvider.GetRequiredService<IdentityMigrationPreflightService>();
            var first = await service.GetPageAsync(null, CancellationToken.None);
            Assert.AreEqual(128, first.RowsInspected);
            Assert.AreEqual(128, first.Evidence.Count);
            Assert.AreEqual(IdentityMigrationPreflightPageState.Incomplete, first.State);
            Assert.IsNotNull(first.NextCursor);

            var second = await service.GetPageAsync(first.NextCursor, CancellationToken.None);
            Assert.AreEqual(IdentityMigrationPreflightPhase.HorseRoots, second.Phase);
            Assert.AreEqual(1, second.RowsInspected);
            Assert.AreEqual(1, second.Evidence.Count);
            var allIds = first.Evidence.Concat(second.Evidence).Select(item => item.HorseId).ToHashSet(StringComparer.Ordinal);
            Assert.AreEqual(129, allIds.Count);
            Assert.IsTrue(allIds.Contains(registeredId));
            Assert.IsTrue(allIds.Contains("horse-00000128-0000-0000-0000-000000000000"));
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    [TestMethod]
    public async Task RootPhaseSqlQueryBudgetDoesNotGrowWithPageRowsAndUsesKeysetPlan()
    {
        var one = await RunRootPageWithStatementTraceAsync(1);
        var page = await RunRootPageWithStatementTraceAsync(IdentityMigrationPreflightPolicy.PageSize);

        Assert.AreEqual(1, one.RowsInspected);
        Assert.AreEqual(IdentityMigrationPreflightPolicy.PageSize, page.RowsInspected);
        Assert.AreEqual(one.SelectCount, page.SelectCount,
            "The bounded root query budget must not scale with roots in a 128-row page.");
        Assert.IsTrue(page.SelectCount <= 20, $"Unexpected bounded SELECT count: {page.SelectCount}.");
        Assert.IsNotNull(page.RootKeySql);
        StringAssert.Contains(page.RootKeySql!, "LIMIT");
        Assert.IsFalse(page.RootKeySql!.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(page.QueryPlan.Any(line => line.Contains("Horses", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task NoncanonicalAndOversizedKeysBecomeCountsWithoutEchoOrUnsafeCursor()
    {
        var root = Path.Combine(Path.GetTempPath(), "identity-migration-preflight-unsafe-key", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventStorePath = Path.Combine(root, "events.db");
        var oversizedId = "horse-" + new string('x', IdentityMigrationPreflightPolicy.MaximumKeyCharacters + 1);
        const string noncanonicalId = "horse-legacy-opaque";
        try
        {
            var (app, client) = await TestApplicationFactory.CreateAsync($"Data Source={eventStorePath};Pooling=False");
            await using var application = app;
            using var http = client;
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            var registeredId = await RegisterFixtureHorseAsync(http);
            await SeedHorseRowsAsync(eventStorePath, [noncanonicalId, oversizedId]);
            _ = new CollectionPlatformStore(app.Services.GetRequiredService<IOptions<CollectionPlatformOptions>>());

            using var serviceProvider = CreatePreflightServiceProvider(app);
            var service = serviceProvider.GetRequiredService<IdentityMigrationPreflightService>();
            var page = await service.GetPageAsync(null, CancellationToken.None);
            var serialized = JsonSerializer.Serialize(page);

            Assert.AreEqual(IdentityMigrationPreflightPageState.Incomplete, page.State);
            Assert.IsNull(page.NextCursor);
            Assert.AreEqual(1, page.Evidence.Count);
            Assert.AreEqual(registeredId, page.Evidence.Single().HorseId);
            Assert.IsTrue(page.Counts.UnknownShape >= 2);
            Assert.IsFalse(serialized.Contains(noncanonicalId, StringComparison.Ordinal));
            Assert.IsFalse(serialized.Contains(oversizedId, StringComparison.Ordinal));
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    [TestMethod]
    public async Task MalformedRegistrationIsUnknownAndDatabaseRejectsDuplicateSequenceOne()
    {
        var root = Path.Combine(Path.GetTempPath(), "identity-migration-preflight-events", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventStorePath = Path.Combine(root, "events.db");
        try
        {
            var (app, client) = await TestApplicationFactory.CreateAsync($"Data Source={eventStorePath};Pooling=False");
            await using var application = app;
            using var http = client;
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            var duplicateId = await RegisterFixtureHorseAsync(http);
            var malformedId = await RegisterFixtureHorseAsync(http);
            _ = new CollectionPlatformStore(app.Services.GetRequiredService<IOptions<CollectionPlatformOptions>>());
            await AddDuplicateRegistrationAndMalformedEventAsync(eventStorePath, duplicateId, malformedId);

            using var serviceProvider = CreatePreflightServiceProvider(app);
            var service = serviceProvider.GetRequiredService<IdentityMigrationPreflightService>();
            var page = await service.GetPageAsync(null, CancellationToken.None);

            Assert.AreNotEqual(IdentityMigrationPreflightObservationStatus.UnknownShape,
                page.Evidence.Single(item => item.HorseId == duplicateId).Status);
            Assert.AreEqual(IdentityMigrationPreflightObservationStatus.UnknownShape,
                page.Evidence.Single(item => item.HorseId == malformedId).Status);
            Assert.AreEqual(IdentityMigrationPreflightPageState.Incomplete, page.State);
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    [TestMethod]
    public async Task LaterHorseEventsAreExplicitlyUnknownOrTruncatedAndReferenceStatusIsPreserved()
    {
        var root = Path.Combine(Path.GetTempPath(), "identity-migration-preflight-later-events",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventStorePath = Path.Combine(root, "events.db");
        try
        {
            var (app, client) = await TestApplicationFactory.CreateAsync($"Data Source={eventStorePath};Pooling=False");
            await using var application = app;
            using var http = client;
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            var unknownTypeId = await RegisterFixtureHorseAsync(http);
            var malformedId = await RegisterFixtureHorseAsync(http);
            var overLimitId = await RegisterFixtureHorseAsync(http);
            var referencedId = await RegisterFixtureHorseAsync(http);
            _ = new CollectionPlatformStore(app.Services.GetRequiredService<IOptions<CollectionPlatformOptions>>());

            var raceId = $"race-{Guid.NewGuid():D}";
            await using (var seed = new SqliteConnection($"Data Source={eventStorePath};Pooling=False"))
            {
                await seed.OpenAsync();
                await using var transaction = await seed.BeginTransactionAsync();
                await InsertProfileAndRaceReferenceAsync(seed, (SqliteTransaction)transaction, referencedId, raceId);
                await InsertLaterEventAsync(seed, (SqliteTransaction)transaction, unknownTypeId, 2,
                    "{\"event_name\":\"FutureHorseEvent\",\"aggregate_name\":\"HorseAggregate\",\"event_version\":1}",
                    "{}");
                await InsertLaterEventAsync(seed, (SqliteTransaction)transaction, malformedId, 2,
                    "not-json", "{");
                for (var sequence = 2; sequence <= 18; sequence++)
                    await InsertLaterEventAsync(seed, (SqliteTransaction)transaction, overLimitId, sequence,
                        "not-json", "{");
                await transaction.CommitAsync();
            }

            using var serviceProvider = CreatePreflightServiceProvider(app);
            var service = serviceProvider.GetRequiredService<IdentityMigrationPreflightService>();
            var firstPage = await service.GetPageAsync(null, CancellationToken.None);
            Assert.AreEqual(IdentityMigrationPreflightObservationStatus.UnknownShape,
                firstPage.Evidence.Single(item => item.HorseId == unknownTypeId).Status);
            Assert.AreEqual(IdentityMigrationPreflightObservationStatus.UnknownShape,
                firstPage.Evidence.Single(item => item.HorseId == malformedId).Status);
            Assert.AreEqual(IdentityMigrationPreflightObservationStatus.Truncated,
                firstPage.Evidence.Single(item => item.HorseId == overLimitId).Status);

            IdentityMigrationPreflightPage? page = firstPage;
            while (page is not null && page.Phase != IdentityMigrationPreflightPhase.HorseRaceHistories)
            {
                Assert.IsNotNull(page.NextCursor);
                page = await service.GetPageAsync(page.NextCursor, CancellationToken.None);
            }
            Assert.IsNotNull(page);
            var referenced = page.Evidence.Single(item => item.HorseId == referencedId);
            Assert.AreEqual(1, referenced.RelatedRaceCount);
            Assert.AreEqual(IdentityMigrationPreflightObservationStatus.Unverifiable, referenced.Status);
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    private static async Task InsertLaterEventAsync(SqliteConnection connection, SqliteTransaction transaction,
        string horseId, int sequence, string metadata, string data)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO EventEntity (AggregateId, AggregateName, AggregateSequenceNumber, BatchId, Data, Metadata)
            VALUES ($id, 'HorseAggregate', $sequence, $batchId, $data, $metadata);
            """;
        command.Parameters.AddWithValue("$id", horseId);
        command.Parameters.AddWithValue("$sequence", sequence);
        command.Parameters.AddWithValue("$batchId", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$data", data);
        command.Parameters.AddWithValue("$metadata", metadata);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertProfileAndRaceReferenceAsync(SqliteConnection connection,
        SqliteTransaction transaction, string horseId, string raceId)
    {
        await using (var profile = connection.CreateCommand())
        {
            profile.Transaction = transaction;
            profile.CommandText = """
                INSERT INTO JraSubjectProfileReadModel (SubjectId, Name, SourceIdentity, SourceUrl, AcquiredAt, Fields)
                VALUES ($id, 'source-backed', 'jra-horse:2024108210', '', $acquiredAt, '{}');
                """;
            profile.Parameters.AddWithValue("$id", horseId);
            profile.Parameters.AddWithValue("$acquiredAt", DateTimeOffset.UtcNow.ToString("O"));
            await profile.ExecuteNonQueryAsync();
        }

        await using var history = connection.CreateCommand();
        history.Transaction = transaction;
        history.CommandText = "INSERT INTO HorseRaceHistories (HorseId, Entries) VALUES ($id, $entries);";
        history.Parameters.AddWithValue("$id", horseId);
        history.Parameters.AddWithValue("$entries", $"[{{\"RaceId\":\"{raceId}\",\"EntryId\":\"{raceId}-entry\"}}]");
        await history.ExecuteNonQueryAsync();
    }

    [TestMethod]
    public async Task CursorIsRejectedWhenSchemaAssumptionsChangeBetweenPages()
    {
        var root = Path.Combine(Path.GetTempPath(), "identity-migration-preflight-schema-drift", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventStorePath = Path.Combine(root, "events.db");
        try
        {
            var (app, client) = await TestApplicationFactory.CreateAsync($"Data Source={eventStorePath};Pooling=False");
            await using var application = app;
            using var http = client;
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            _ = await RegisterFixtureHorseAsync(http);
            _ = new CollectionPlatformStore(app.Services.GetRequiredService<IOptions<CollectionPlatformOptions>>());
            using var serviceProvider = CreatePreflightServiceProvider(app);
            var service = serviceProvider.GetRequiredService<IdentityMigrationPreflightService>();
            var first = await service.GetPageAsync(null, CancellationToken.None);
            Assert.IsNotNull(first.NextCursor);

            await using (var connection = new SqliteConnection($"Data Source={eventStorePath};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE AddedAfterFirstPage (Value TEXT NOT NULL);";
                await command.ExecuteNonQueryAsync();
            }

            await Assert.ThrowsAsync<IdentityMigrationPreflightInvalidCursorException>(
                () => service.GetPageAsync(first.NextCursor, CancellationToken.None));
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    [TestMethod]
    public async Task SchemaFingerprintIncludesDeclaredColumnTypesAndUnknownPhysicalObjects()
    {
        var root = Path.Combine(Path.GetTempPath(), "identity-migration-preflight-schema-shape",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var textPath = Path.Combine(root, "text.db");
        var blobPath = Path.Combine(root, "blob.db");
        try
        {
            await CreateSchemaFixtureAsync(textPath, "TEXT", includeUnknownView: false);
            await CreateSchemaFixtureAsync(blobPath, "BLOB", includeUnknownView: true);
            var knownNames = new[] { "Evidence" };
            var expectedColumns = new Dictionary<string, string[]> { ["Evidence"] = ["Value"] };
            var requiredNames = new[] { "Evidence" };

            var textSchema = await IdentityMigrationPreflightSqliteSchema.InspectAsync(
                IdentityMigrationPreflightSurface.EventStore, textPath, knownNames, expectedColumns,
                requiredNames, CancellationToken.None);
            var blobSchema = await IdentityMigrationPreflightSqliteSchema.InspectAsync(
                IdentityMigrationPreflightSurface.EventStore, blobPath, knownNames, expectedColumns,
                requiredNames, CancellationToken.None);

            Assert.AreEqual(IdentityMigrationPreflightObservationStatus.Observed, textSchema.Status);
            Assert.AreEqual(IdentityMigrationPreflightObservationStatus.UnknownShape, blobSchema.Status);
            Assert.AreEqual(1, blobSchema.UnknownObjectCount);
            Assert.AreNotEqual(textSchema.AssumptionFingerprint, blobSchema.AssumptionFingerprint);
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    private static async Task CreateSchemaFixtureAsync(string path, string valueType, bool includeUnknownView)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE TABLE Evidence (Value {valueType} NOT NULL);"
            + (includeUnknownView ? " CREATE VIEW Unexpected AS SELECT Value FROM Evidence;" : string.Empty);
        await command.ExecuteNonQueryAsync();
    }

    [TestMethod]
    public async Task BusyAndCancelledAdmissionDoNotQueueAndReleaseForTheNextRequest()
    {
        var root = Path.Combine(Path.GetTempPath(), "identity-migration-preflight-admission", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventStorePath = Path.Combine(root, "events.db");
        try
        {
            var (app, client) = await TestApplicationFactory.CreateAsync($"Data Source={eventStorePath};Pooling=False");
            await using var application = app;
            using var http = client;
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            _ = await RegisterFixtureHorseAsync(http);
            _ = new CollectionPlatformStore(app.Services.GetRequiredService<IOptions<CollectionPlatformOptions>>());
            using var serviceProvider = CreatePreflightServiceProvider(app);
            var service = serviceProvider.GetRequiredService<IdentityMigrationPreflightService>();
            var admission = (SemaphoreSlim)typeof(IdentityMigrationPreflightService)
                .GetField("Admission", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null)!;

            Assert.IsTrue(admission.Wait(0));
            try
            {
                await Assert.ThrowsAsync<IdentityMigrationPreflightBusyException>(
                    () => service.GetPageAsync(null, CancellationToken.None));
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                await Assert.ThrowsAsync<OperationCanceledException>(
                    () => service.GetPageAsync(null, cancelled.Token));
            }
            finally
            {
                admission.Release();
            }

            var page = await service.GetPageAsync(null, CancellationToken.None);
            Assert.AreEqual(IdentityMigrationPreflightPhase.HorseRoots, page.Phase);
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    [TestMethod]
    public async Task LockedReadIsUnavailableAndSucceedsAfterStoreReopens()
    {
        var root = Path.Combine(Path.GetTempPath(), "identity-migration-preflight-locked-read",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventStorePath = Path.Combine(root, "events.db");
        try
        {
            var (app, client) = await TestApplicationFactory.CreateAsync($"Data Source={eventStorePath};Pooling=False");
            await using var application = app;
            using var http = client;
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            _ = await RegisterFixtureHorseAsync(http);
            _ = new CollectionPlatformStore(app.Services.GetRequiredService<IOptions<CollectionPlatformOptions>>());
            using var serviceProvider = CreatePreflightServiceProvider(app);
            var service = serviceProvider.GetRequiredService<IdentityMigrationPreflightService>();

            await using var blocker = new SqliteConnection($"Data Source={eventStorePath};Pooling=False");
            await blocker.OpenAsync();
            await using (var lockCommand = blocker.CreateCommand())
            {
                lockCommand.CommandText = "PRAGMA locking_mode=EXCLUSIVE; BEGIN EXCLUSIVE;";
                await lockCommand.ExecuteNonQueryAsync();
            }

            await Assert.ThrowsAsync<IdentityMigrationPreflightUnavailableException>(
                () => service.GetPageAsync(null, CancellationToken.None));

            await using (var unlock = blocker.CreateCommand())
            {
                unlock.CommandText = "ROLLBACK; PRAGMA locking_mode=NORMAL;";
                await unlock.ExecuteNonQueryAsync();
            }
            // SQLite keeps an EXCLUSIVE connection lock until the connection itself closes,
            // even after leaving EXCLUSIVE locking mode.
            await blocker.DisposeAsync();
            var retry = await service.GetPageAsync(null, CancellationToken.None);
            Assert.AreEqual(IdentityMigrationPreflightPhase.HorseRoots, retry.Phase);
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    [TestMethod]
    public async Task CancellationAfterAdmissionReleasesSlotForRetry()
    {
        var root = Path.Combine(Path.GetTempPath(), "identity-migration-preflight-admitted-cancel",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventStorePath = Path.Combine(root, "events.db");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        Task<IdentityMigrationPreflightPage>? inFlight = null;
        try
        {
            var (app, client) = await TestApplicationFactory.CreateAsync($"Data Source={eventStorePath};Pooling=False");
            await using var application = app;
            using var http = client;
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            _ = await RegisterFixtureHorseAsync(http);
            _ = new CollectionPlatformStore(app.Services.GetRequiredService<IOptions<CollectionPlatformOptions>>());
            using var serviceProvider = CreatePreflightServiceProvider(app);
            var service = serviceProvider.GetRequiredService<IdentityMigrationPreflightService>();
            using (IdentityMigrationPreflightSqliteSchema.ObserveStatements(statement =>
                   {
                       entered.Set();
                       release.Wait(TimeSpan.FromSeconds(5));
                   }))
            {
                inFlight = Task.Run(() => service.GetPageAsync(null, cancellation.Token));
                var reachedRead = entered.Wait(TimeSpan.FromSeconds(3));
                try
                {
                    Assert.IsTrue(reachedRead, "The request did not reach its SQLite read within the bounded test wait.");
                    await Assert.ThrowsAsync<IdentityMigrationPreflightBusyException>(
                        () => service.GetPageAsync(null, CancellationToken.None));
                    cancellation.Cancel();
                }
                finally
                {
                    release.Set();
                }
            }

            await Assert.ThrowsAsync<OperationCanceledException>(async () => await inFlight!);
            var retry = await service.GetPageAsync(null, CancellationToken.None);
            Assert.AreEqual(IdentityMigrationPreflightPhase.HorseRoots, retry.Phase);
        }
        finally
        {
            release.Set();
            cancellation?.Cancel();
            if (inFlight is not null)
            {
                try { await inFlight.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (OperationCanceledException) { }
                catch (SqliteException) { }
                catch (TimeoutException) { }
            }
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    [TestMethod]
    public async Task OversizedRegistrationJsonIsNotMaterializedOrEchoed()
    {
        var root = Path.Combine(Path.GetTempPath(), "identity-migration-preflight-oversized-json",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventStorePath = Path.Combine(root, "events.db");
        var sentinel = new string('Z', IdentityMigrationPreflightPolicy.MaximumPayloadBytes + 17);
        try
        {
            var (app, client) = await TestApplicationFactory.CreateAsync($"Data Source={eventStorePath};Pooling=False");
            await using var application = app;
            using var http = client;
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            var horseId = await RegisterFixtureHorseAsync(http);
            _ = new CollectionPlatformStore(app.Services.GetRequiredService<IOptions<CollectionPlatformOptions>>());
            await using (var connection = new SqliteConnection($"Data Source={eventStorePath};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var update = connection.CreateCommand();
                update.CommandText = "UPDATE EventEntity SET Data=$data WHERE AggregateId=$id AND AggregateName='HorseAggregate' AND AggregateSequenceNumber=1;";
                update.Parameters.AddWithValue("$data", sentinel);
                update.Parameters.AddWithValue("$id", horseId);
                Assert.AreEqual(1, await update.ExecuteNonQueryAsync());
            }

            using var serviceProvider = CreatePreflightServiceProvider(app);
            var page = await serviceProvider.GetRequiredService<IdentityMigrationPreflightService>()
                .GetPageAsync(null, CancellationToken.None);
            var evidence = page.Evidence.Single(item => item.HorseId == horseId);
            Assert.AreEqual(IdentityMigrationPreflightObservationStatus.Truncated, evidence.Status);
            Assert.IsFalse(JsonSerializer.Serialize(page).Contains(sentinel, StringComparison.Ordinal));
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    [TestMethod]
    public async Task FiveNonRootSourcesSurfaceOrphanIdsAsUnverifiable()
    {
        var root = Path.Combine(Path.GetTempPath(), "identity-migration-preflight-orphans", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventStorePath = Path.Combine(root, "events.db");
        try
        {
            var (app, client) = await TestApplicationFactory.CreateAsync($"Data Source={eventStorePath};Pooling=False");
            await using var application = app;
            using var http = client;
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            var sourceHorseId = await RegisterFixtureHorseAsync(http);
            var orphanEventId = $"horse-{Guid.NewGuid():D}";
            var orphanProfileId = $"horse-{Guid.NewGuid():D}";
            var orphanHistoryId = $"horse-{Guid.NewGuid():D}";
            var orphanWeightId = $"horse-{Guid.NewGuid():D}";
            var orphanCollectionId = $"horse-{Guid.NewGuid():D}";
            await SeedOrphanSourcesAsync(eventStorePath, sourceHorseId, orphanEventId,
                orphanProfileId, orphanHistoryId, orphanWeightId);

            var collectionStore = new CollectionPlatformStore(app.Services.GetRequiredService<IOptions<CollectionPlatformOptions>>());
            await collectionStore.RegisterDefinitionAsync(new("preflight-orphan-profile"), "Horse profile",
                CollectionResourceType.Horse, 1, "preflight", false);
            await collectionStore.RequestAsync(new(CollectionResourceType.Horse, "JRA", orphanCollectionId),
                new("preflight-orphan-profile"), 1, CollectionReason.Initial, DateTimeOffset.UtcNow,
                attributes: new Dictionary<string, string> { ["sourceIdentity"] = "jra-horse:1234567890" });

            using var serviceProvider = CreatePreflightServiceProvider(app);
            var service = serviceProvider.GetRequiredService<IdentityMigrationPreflightService>();
            var evidenceByPhase = new Dictionary<IdentityMigrationPreflightPhase, List<IdentityMigrationPreflightHorseEvidence>>();
            IdentityMigrationPreflightPage? page = await service.GetPageAsync(null, CancellationToken.None);
            for (var request = 0; page is not null && request < 12; request++)
            {
                evidenceByPhase[page.Phase] = page.Evidence.ToList();
                if (page.NextCursor is null) break;
                page = await service.GetPageAsync(page.NextCursor, CancellationToken.None);
            }

            AssertEvidence(evidenceByPhase, IdentityMigrationPreflightPhase.HorseEvents, orphanEventId);
            AssertEvidence(evidenceByPhase, IdentityMigrationPreflightPhase.JraProfiles, orphanProfileId);
            AssertEvidence(evidenceByPhase, IdentityMigrationPreflightPhase.HorseRaceHistories, orphanHistoryId);
            AssertEvidence(evidenceByPhase, IdentityMigrationPreflightPhase.HorseWeightHistories, orphanWeightId);
            AssertEvidence(evidenceByPhase, IdentityMigrationPreflightPhase.CollectionResources, orphanCollectionId);
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    [TestMethod]
    public async Task ConfiguredMissingOrUnsupportedStoreAndOptionalExternalQueueStayDistinct()
    {
        var root = Path.Combine(Path.GetTempPath(), "identity-migration-preflight-configured-surfaces", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var (app, client) = await TestApplicationFactory.CreateAsync($"Data Source={Path.Combine(root, "events.db")};Pooling=False");
            await using var application = app;
            using var http = client;
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            var collectionOptions = Options.Create(new CollectionPlatformOptions
            {
                StateDirectory = Path.Combine(root, "collection"),
            });
            _ = new CollectionPlatformStore(collectionOptions);
            var scheduleDirectory = Path.Combine(root, "schedule");
            Directory.CreateDirectory(scheduleDirectory);
            var schedulePath = Path.Combine(scheduleDirectory, "prediction-executions.db");
            var reader = new IdentityMigrationPreflightConfiguredSurfaceReader(
                collectionOptions,
                Options.Create(new PredictionScheduleOptions { StateDirectory = scheduleDirectory }),
                Options.Create(new CollectionQueueOptions { Provider = "Sqs" }),
                app.Services.GetRequiredService<IWebHostEnvironment>());
            var missingObservations = await reader.InspectAsync(CancellationToken.None);
            Assert.AreEqual(IdentityMigrationPreflightObservationStatus.Unavailable,
                missingObservations.Single(item => item.Surface == IdentityMigrationPreflightSurface.PredictionSchedule).Status);
            await using (var scheduleConnection = new SqliteConnection($"Data Source={schedulePath};Pooling=False"))
            {
                await scheduleConnection.OpenAsync();
                await using var command = scheduleConnection.CreateCommand();
                command.CommandText = "CREATE TABLE prediction_candidates (RaceId TEXT NOT NULL);";
                await command.ExecuteNonQueryAsync();
            }

            var observations = await reader.InspectAsync(CancellationToken.None);
            Assert.AreEqual(IdentityMigrationPreflightObservationStatus.Observed,
                observations.Single(item => item.Surface == IdentityMigrationPreflightSurface.CollectionPlatform).Status);
            Assert.AreEqual(IdentityMigrationPreflightObservationStatus.Unsupported,
                observations.Single(item => item.Surface == IdentityMigrationPreflightSurface.PredictionSchedule).Status);
            Assert.AreEqual(IdentityMigrationPreflightObservationStatus.NotConfigured,
                observations.Single(item => item.Surface == IdentityMigrationPreflightSurface.LocalCollectionQueue).Status);
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    [TestMethod]
    public async Task RaceHistoryPhaseDoesNotReadOtherAggregateEventsToInventHorseNumberEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "identity-migration-preflight-history", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventStorePath = Path.Combine(root, "events.db");
        try
        {
            var (app, client) = await TestApplicationFactory.CreateAsync($"Data Source={eventStorePath};Pooling=False");
            await using var application = app;
            using var http = client;
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            var horseId = $"horse-{Guid.NewGuid():D}";
            using var registration = await http.PostAsJsonAsync("/api/horses",
                new RegisterHorseRequest(new($"History {Guid.NewGuid():N}", "history-normalized", "牡",
                    new DateOnly(2022, 3, 4), horseId)));
            Assert.AreEqual(HttpStatusCode.Created, registration.StatusCode);

            var raceId = $"race-{Guid.NewGuid():D}";
            var entryId = $"{raceId}-entry-{horseId}";
            await using (var seed = new SqliteConnection($"Data Source={eventStorePath};Pooling=False"))
            {
                await seed.OpenAsync();
                await using var transaction = await seed.BeginTransactionAsync();
                await using (var history = seed.CreateCommand())
                {
                    history.Transaction = (SqliteTransaction)transaction;
                    history.CommandText = "INSERT INTO HorseRaceHistories (HorseId, Entries) VALUES ($horseId, $entries);";
                    history.Parameters.AddWithValue("$horseId", horseId);
                    history.Parameters.AddWithValue("$entries", $"[{{\"RaceId\":\"{raceId}\",\"EntryId\":\"{entryId}\"}}]");
                    await history.ExecuteNonQueryAsync();
                }
                await using (var eventRow = seed.CreateCommand())
                {
                    eventRow.Transaction = (SqliteTransaction)transaction;
                    eventRow.CommandText = """
                        INSERT INTO EventEntity (AggregateId, AggregateName, AggregateSequenceNumber, BatchId, Data, Metadata)
                        VALUES ($raceId, 'RaceAggregate', 1, $batchId, $data, $metadata);
                        """;
                    eventRow.Parameters.AddWithValue("$raceId", raceId);
                    eventRow.Parameters.AddWithValue("$batchId", Guid.NewGuid().ToString("D"));
                    eventRow.Parameters.AddWithValue("$data", "not-json-and-outside-the-approved-event-allowlist");
                    eventRow.Parameters.AddWithValue("$metadata", "not-json");
                    await eventRow.ExecuteNonQueryAsync();
                }
                await transaction.CommitAsync();
            }

            _ = new CollectionPlatformStore(app.Services.GetRequiredService<IOptions<CollectionPlatformOptions>>());
            using var serviceProvider = CreatePreflightServiceProvider(app);
            var service = serviceProvider.GetRequiredService<IdentityMigrationPreflightService>();
            IdentityMigrationPreflightPage? page = await service.GetPageAsync(null, CancellationToken.None);
            while (page is not null && page.Phase != IdentityMigrationPreflightPhase.HorseRaceHistories)
            {
                Assert.IsNotNull(page.NextCursor);
                page = await service.GetPageAsync(page.NextCursor, CancellationToken.None);
            }

            Assert.IsNotNull(page);
            var evidence = page.Evidence.Single(item => item.HorseId == horseId);
            Assert.AreEqual(1, evidence.RelatedRaceCount);
            Assert.IsFalse(evidence.RelatedRacesTruncated);
            Assert.AreEqual(0, evidence.RelatedRaces.Count);
            Assert.AreEqual(IdentityMigrationPreflightObservationStatus.Unverifiable, evidence.Status);
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    [TestMethod]
    public async Task RelatedRaceEvidenceIsBoundedAndMarksTheSeventeenthReferenceTruncated()
    {
        var root = Path.Combine(Path.GetTempPath(), "identity-migration-preflight-reference-bound", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventStorePath = Path.Combine(root, "events.db");
        try
        {
            var (app, client) = await TestApplicationFactory.CreateAsync($"Data Source={eventStorePath};Pooling=False");
            await using var application = app;
            using var http = client;
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            var horseId = await RegisterFixtureHorseAsync(http);
            var entries = JsonSerializer.Serialize(Enumerable.Range(1, 17)
                .Select(number => new { RaceId = $"race-{Guid.Parse($"{number:D8}-0000-0000-0000-000000000000"):D}" }));
            await using (var connection = new SqliteConnection($"Data Source={eventStorePath};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO HorseRaceHistories (HorseId, Entries) VALUES ($id, $entries);";
                command.Parameters.AddWithValue("$id", horseId);
                command.Parameters.AddWithValue("$entries", entries);
                await command.ExecuteNonQueryAsync();
            }
            _ = new CollectionPlatformStore(app.Services.GetRequiredService<IOptions<CollectionPlatformOptions>>());
            using var serviceProvider = CreatePreflightServiceProvider(app);
            var service = serviceProvider.GetRequiredService<IdentityMigrationPreflightService>();
            IdentityMigrationPreflightPage? page = await service.GetPageAsync(null, CancellationToken.None);
            while (page is not null && page.Phase != IdentityMigrationPreflightPhase.HorseRaceHistories)
            {
                Assert.IsNotNull(page.NextCursor);
                page = await service.GetPageAsync(page.NextCursor, CancellationToken.None);
            }

            Assert.IsNotNull(page);
            var evidence = page.Evidence.Single(item => item.HorseId == horseId);
            Assert.AreEqual(16, evidence.RelatedRaceCount);
            Assert.IsTrue(evidence.RelatedRacesTruncated);
            Assert.AreEqual(0, evidence.RelatedRaces.Count);
            Assert.AreEqual(IdentityMigrationPreflightObservationStatus.Truncated, evidence.Status);
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    private static async Task<SqliteConnection> OpenReadOnlyAsync(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA query_only=ON;";
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    private static async Task SeedHorseRowsAsync(string eventStorePath, int count) =>
        await SeedHorseRowsAsync(eventStorePath, Enumerable.Range(1, count)
            .Select(value => $"horse-{value:D8}-0000-0000-0000-000000000000").ToArray());

    private static async Task<string> RegisterFixtureHorseAsync(HttpClient http)
    {
        var horseId = $"horse-{Guid.NewGuid():D}";
        using var response = await http.PostAsJsonAsync("/api/horses",
            new RegisterHorseRequest(new($"Preflight {Guid.NewGuid():N}", "preflight-fixture", "牡",
                new DateOnly(2021, 2, 3), horseId)));
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return horseId;
    }

    private static async Task SeedHorseRowsAsync(string eventStorePath, IReadOnlyList<string> horseIds)
    {
        await using var connection = new SqliteConnection($"Data Source={eventStorePath};Pooling=False");
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var horseId in horseIds)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO Horses (HorseId, RegisteredName, NormalizedName, SexCode, BirthDate, OwnerName, Aliases)
                VALUES ($id, 'bounded fixture', 'bounded-fixture', NULL, NULL, NULL, '[]');
                """;
            command.Parameters.AddWithValue("$id", horseId);
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    private static async Task AddDuplicateRegistrationAndMalformedEventAsync(
        string eventStorePath, string duplicateId, string malformedId)
    {
        await using var connection = new SqliteConnection($"Data Source={eventStorePath};Pooling=False");
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var duplicate = connection.CreateCommand())
        {
            duplicate.Transaction = (SqliteTransaction)transaction;
            duplicate.CommandText = """
                INSERT INTO EventEntity (AggregateId, AggregateName, AggregateSequenceNumber, BatchId, Data, Metadata)
                SELECT AggregateId, AggregateName, AggregateSequenceNumber, $batchId, Data, Metadata
                FROM EventEntity
                WHERE AggregateId = $id AND AggregateName = $aggregateName AND AggregateSequenceNumber = 1
                LIMIT 1;
                """;
            duplicate.Parameters.AddWithValue("$id", duplicateId);
            duplicate.Parameters.AddWithValue("$aggregateName", "HorseAggregate");
            duplicate.Parameters.AddWithValue("$batchId", Guid.NewGuid().ToString("D"));
            await Assert.ThrowsAsync<SqliteException>(() => duplicate.ExecuteNonQueryAsync());
        }
        await using (var malformed = connection.CreateCommand())
        {
            malformed.Transaction = (SqliteTransaction)transaction;
            malformed.CommandText = """
                UPDATE EventEntity SET Data = '{', Metadata = '{}'
                WHERE AggregateId = $id AND AggregateName = $aggregateName AND AggregateSequenceNumber = 1;
                """;
            malformed.Parameters.AddWithValue("$id", malformedId);
            malformed.Parameters.AddWithValue("$aggregateName", "HorseAggregate");
            Assert.AreEqual(1, await malformed.ExecuteNonQueryAsync());
        }
        await transaction.CommitAsync();
    }

    private static async Task SeedOrphanSourcesAsync(string eventStorePath, string sourceHorseId,
        string orphanEventId, string orphanProfileId, string orphanHistoryId, string orphanWeightId)
    {
        await using var connection = new SqliteConnection($"Data Source={eventStorePath};Pooling=False");
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        string data;
        string metadata;
        await using (var source = connection.CreateCommand())
        {
            source.Transaction = (SqliteTransaction)transaction;
            source.CommandText = """
                SELECT Data, Metadata FROM EventEntity
                WHERE AggregateId = $id AND AggregateName = 'HorseAggregate' AND AggregateSequenceNumber = 1;
                """;
            source.Parameters.AddWithValue("$id", sourceHorseId);
            await using var reader = await source.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            data = reader.GetString(0);
            metadata = reader.GetString(1);
        }

        await using (var orphanEvent = connection.CreateCommand())
        {
            orphanEvent.Transaction = (SqliteTransaction)transaction;
            orphanEvent.CommandText = """
                INSERT INTO EventEntity (AggregateId, AggregateName, AggregateSequenceNumber, BatchId, Data, Metadata)
                VALUES ($id, 'HorseAggregate', 1, $batchId, $data, $metadata);
                """;
            orphanEvent.Parameters.AddWithValue("$id", orphanEventId);
            orphanEvent.Parameters.AddWithValue("$batchId", Guid.NewGuid().ToString("D"));
            orphanEvent.Parameters.AddWithValue("$data", data);
            orphanEvent.Parameters.AddWithValue("$metadata", metadata);
            await orphanEvent.ExecuteNonQueryAsync();
        }
        await using (var profile = connection.CreateCommand())
        {
            profile.Transaction = (SqliteTransaction)transaction;
            profile.CommandText = """
                INSERT INTO JraSubjectProfileReadModel (SubjectId, Name, SourceIdentity, SourceUrl, AcquiredAt, Fields)
                VALUES ($id, 'orphan profile', 'jra-horse:1234567890', '', $at, '{}');
                """;
            profile.Parameters.AddWithValue("$id", orphanProfileId);
            profile.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            await profile.ExecuteNonQueryAsync();
        }
        await using (var history = connection.CreateCommand())
        {
            history.Transaction = (SqliteTransaction)transaction;
            history.CommandText = "INSERT INTO HorseRaceHistories (HorseId, Entries) VALUES ($id, '[]');";
            history.Parameters.AddWithValue("$id", orphanHistoryId);
            await history.ExecuteNonQueryAsync();
        }
        await using (var weight = connection.CreateCommand())
        {
            weight.Transaction = (SqliteTransaction)transaction;
            weight.CommandText = "INSERT INTO HorseWeightHistories (HorseId, WeightHistory) VALUES ($id, '[]');";
            weight.Parameters.AddWithValue("$id", orphanWeightId);
            await weight.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    private static void AssertEvidence(
        IReadOnlyDictionary<IdentityMigrationPreflightPhase, List<IdentityMigrationPreflightHorseEvidence>> evidenceByPhase,
        IdentityMigrationPreflightPhase phase, string horseId)
    {
        Assert.IsTrue(evidenceByPhase.TryGetValue(phase, out var evidence), $"Missing scan phase {phase}.");
        var row = evidence!.Single(item => item.HorseId == horseId);
        Assert.AreEqual(IdentityMigrationPreflightObservationStatus.Unverifiable, row.Status);
    }

    private static ServiceProvider CreatePreflightServiceProvider(WebApplication app)
    {
        var services = new ServiceCollection();
        services.AddSingleton(app.Services.GetRequiredService<IDbContextProvider<EventStoreDbContext>>());
        services.AddSingleton(app.Services.GetRequiredService<IOptions<CollectionPlatformOptions>>());
        services.AddSingleton(app.Services.GetRequiredService<IOptions<PredictionScheduleOptions>>());
        services.AddSingleton(app.Services.GetRequiredService<IOptions<CollectionQueueOptions>>());
        services.AddSingleton(app.Services.GetRequiredService<IWebHostEnvironment>());
        services.AddSingleton(app.Services.GetRequiredService<IDataProtectionProvider>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IdentityMigrationPreflightEventStoreReader>(provider =>
            new(provider.GetRequiredService<IDbContextProvider<EventStoreDbContext>>()));
        services.AddSingleton<IdentityMigrationPreflightConfiguredSurfaceReader>(provider => new(
            provider.GetRequiredService<IOptions<CollectionPlatformOptions>>(),
            provider.GetRequiredService<IOptions<PredictionScheduleOptions>>(),
            provider.GetRequiredService<IOptions<CollectionQueueOptions>>(),
            provider.GetRequiredService<IWebHostEnvironment>()));
        services.AddSingleton<IdentityMigrationPreflightCursorProtector>(provider => new(
            provider.GetRequiredService<IDataProtectionProvider>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IdentityMigrationPreflightService>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class DeferredEventStoreContextProvider(string path) : IDbContextProvider<EventStoreDbContext>
    {
        private readonly Microsoft.EntityFrameworkCore.DbContextOptions<EventStoreDbContext> _options =
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<EventStoreDbContext>()
                .UseSqlite($"Data Source={path};Pooling=False").Options;

        public EventStoreDbContext CreateContext() => new(_options);
    }

    private static async Task<string[]> SnapshotAsync(IReadOnlyList<string> paths)
    {
        var snapshot = new List<string>();
        foreach (var path in paths)
        {
            await using var connection = await OpenReadOnlyAsync(path);
            var tables = new List<string>();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
            }

            foreach (var table in tables)
            {
                snapshot.Add($"{Path.GetFileName(path)}:{table}");
                await using var command = connection.CreateCommand();
                command.CommandText = $"SELECT * FROM \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\";";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var row = new StringBuilder();
                    for (var index = 0; index < reader.FieldCount; index++)
                    {
                        var value = reader.IsDBNull(index) ? "<null>" : Convert.ToString(reader.GetValue(index),
                            System.Globalization.CultureInfo.InvariantCulture) ?? "";
                        row.Append(value.Length).Append(':').Append(value).Append('|');
                    }
                    snapshot.Add(row.ToString());
                }
            }
        }
        return snapshot.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static async Task<(int RowsInspected, int SelectCount, string? RootKeySql, string[] QueryPlan)>
        RunRootPageWithStatementTraceAsync(int totalRoots)
    {
        var root = Path.Combine(Path.GetTempPath(), "identity-migration-preflight-query-budget",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventStorePath = Path.Combine(root, "events.db");
        try
        {
            var (app, client) = await TestApplicationFactory.CreateAsync($"Data Source={eventStorePath};Pooling=False");
            await using var application = app;
            using var http = client;
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            _ = await RegisterFixtureHorseAsync(http);
            if (totalRoots > 1) await SeedHorseRowsAsync(eventStorePath, totalRoots - 1);
            _ = new CollectionPlatformStore(app.Services.GetRequiredService<IOptions<CollectionPlatformOptions>>());

            var statements = new List<string>();
            using var serviceProvider = CreatePreflightServiceProvider(app);
            var service = serviceProvider.GetRequiredService<IdentityMigrationPreflightService>();
            IdentityMigrationPreflightPage page;
            using (IdentityMigrationPreflightSqliteSchema.ObserveStatements(statements.Add))
                page = await service.GetPageAsync(null, CancellationToken.None);

            var selects = statements.Where(sql => sql.TrimStart().StartsWith("SELECT",
                StringComparison.OrdinalIgnoreCase)).ToArray();
            var rootKeySql = selects.SingleOrDefault(sql => sql.Contains("FROM Horses", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("GROUP BY HorseId", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("ORDER BY HorseId", StringComparison.OrdinalIgnoreCase));
            var queryPlan = Array.Empty<string>();
            if (rootKeySql is not null)
            {
                await using var connection = await OpenReadOnlyAsync(eventStorePath);
                await using var explain = connection.CreateCommand();
                explain.CommandText = "EXPLAIN QUERY PLAN " + rootKeySql;
                var lines = new List<string>();
                await using var reader = await explain.ExecuteReaderAsync();
                while (await reader.ReadAsync()) lines.Add(reader.GetString(3));
                queryPlan = lines.ToArray();
            }
            return (page.RowsInspected, selects.Length, rootKeySql, queryPlan);
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    private static async Task DeleteDirectoryEventuallyAsync(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4) { await Task.Delay(100); }
            catch (UnauthorizedAccessException) when (attempt < 4) { await Task.Delay(100); }
        }
    }
}
