using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Text.Json;
using HorseRacingPrediction.Api;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Horses;
using HorseRacingPrediction.Contracts.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityDiagnosticServiceTests
{
    [TestMethod]
    public void RegisteredEventAndSourceParsers_FailClosedForWrongTypesUnknownShapeAndUnsafeUrl()
    {
        Assert.IsTrue(HorseIdentityDiagnosticService.IsCanonicalHorseId("horse-00000000-0000-0000-0000-000000000001"));
        Assert.IsFalse(HorseIdentityDiagnosticService.IsCanonicalHorseId("horse-00000000-0000-0000-0000-000000000001".ToUpperInvariant()));
        Assert.IsFalse(HorseIdentityDiagnosticService.IsCanonicalHorseId("horse-not-a-guid"));

        Assert.IsNull(ValidateMetadata("{\"event_name\":\"HorseRegistered\",\"event_version\":\"1\",\"aggregate_name\":\"HorseAggregate\"}"));
        Assert.AreEqual("UnexpectedEventType", ValidateMetadata("{\"event_name\":\"Other\",\"event_version\":1,\"aggregate_name\":\"HorseAggregate\"}"));
        Assert.AreEqual("UnexpectedAggregateMetadata", ValidateMetadata("{\"event_name\":\"HorseRegistered\",\"event_version\":1,\"aggregate_name\":\"Other\"}"));
        Assert.AreEqual("MissingEventVersion", ValidateMetadata("{\"event_name\":\"HorseRegistered\",\"event_version\":true,\"aggregate_name\":\"HorseAggregate\"}"));

        var validEvent = """
            {"RegisteredName":"Horse","NormalizedName":null,"SexCode":"牡","BirthDate":"2020-01-02",
             "OwnerName":null,"BreederName":null,"SireName":null,"DamName":null,"DamsireName":null,"CoatColor":null}
            """;
        Assert.IsNull(ValidateShape(validEvent));
        Assert.AreEqual("InvalidEventFieldType", ValidateShape(validEvent.Replace("\"RegisteredName\":\"Horse\"", "\"RegisteredName\":42", StringComparison.Ordinal)));
        Assert.AreEqual("UnknownEventField", ValidateShape(validEvent.Replace("\"CoatColor\":null", "\"CoatColor\":null,\"Unknown\":\"x\"", StringComparison.Ordinal)));
        Assert.AreEqual("MissingEventField", ValidateShape(validEvent.Replace("\"DamsireName\":null,", string.Empty, StringComparison.Ordinal)));

        var createSource = typeof(HorseIdentityDiagnosticService).GetMethod(
            "CreateSource", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var unsafeUrl = (HorseSourceEvidenceDto)createSource.Invoke(null,
            ["CollectionResource", "jra-horse:1234567890", "https://www.jra.go.jp:8443/JRADB/accessU.html?CNAME=pw01dud002024123456/FC", "Read"])!;
        Assert.AreEqual("Supported", unsafeUrl.Status);
        Assert.AreEqual("Invalid", unsafeUrl.SourceUrlStatus);
        Assert.IsNull(unsafeUrl.SourceUrlHorseIdentity);

        var conflicting = (HorseSourceEvidenceDto)createSource.Invoke(null,
            ["CollectionResource", "jra-horse:1234567890", "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002024999999/FC", "Read"])!;
        Assert.AreEqual("Conflicting", conflicting.Status);
        Assert.AreEqual("jra-horse:1234567890", conflicting.NormalizedSourceIdentity);
        Assert.AreEqual("jra-horse:2024999999", conflicting.SourceUrlHorseIdentity);

        var missing = (HorseSourceEvidenceDto)createSource.Invoke(null,
            ["ProfileReadModel", null, null, "Read"])!;
        Assert.AreEqual("Missing", missing.Status);
        Assert.AreEqual("Missing", missing.SourceUrlStatus);
        Assert.IsNull(missing.NormalizedSourceIdentity);
        Assert.IsNull(missing.SourceUrlHorseIdentity);

        var urlOnly = (HorseSourceEvidenceDto)createSource.Invoke(null,
            ["ProfileReadModel", null,
                "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002024123456/FC", "Read"])!;
        Assert.AreEqual("Missing", urlOnly.Status);
        Assert.AreEqual("Supported", urlOnly.SourceUrlStatus);
        Assert.IsNull(urlOnly.NormalizedSourceIdentity);
        Assert.AreEqual("jra-horse:2024123456", urlOnly.SourceUrlHorseIdentity);

        var buildComparisons = typeof(HorseIdentityDiagnosticService).GetMethod(
            "BuildComparisons", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var sourceOnlyComparisons = (IReadOnlyList<HorseIdentityComparisonDto>)buildComparisons.Invoke(null,
            ["horse-00000000-0000-0000-0000-000000000001", null,
                new[] { new HorseSourceEvidenceDto("CollectionResource", "Supported", "jra-horse:1234567890",
                    "jra-horse:1234567890", "Valid", null) }])!;
        Assert.AreEqual(1, sourceOnlyComparisons.Count);
        Assert.AreEqual("Source:CollectionResource", sourceOnlyComparisons[0].Rule);
        Assert.AreEqual("Supported", sourceOnlyComparisons[0].EvidenceStatus);
        Assert.AreEqual(DeterministicIdGenerator.BuildHorseId("ignored",
                "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud001234567890/FC"),
            sourceOnlyComparisons[0].CandidateHorseId);
    }

    [TestMethod]
    public async Task DiagnosticRead_LeavesRowsSchemaControlsAndQueueUnchangedAndRejectsWritesOnReadonlyConnection()
    {
        var root = Path.Combine(Path.GetTempPath(), "horse-identity-diagnostic-service", Guid.NewGuid().ToString("N"));
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
                new RegisterHorseRequest(new($"Service test {Guid.NewGuid():N}", "service-normalized", "牡", new DateOnly(2020, 1, 2), horseId)));
            Assert.AreEqual(HttpStatusCode.Created, registration.StatusCode);

            var store = app.Services.GetRequiredService<CollectionPlatformStore>();
            await store.RegisterDefinitionAsync(new("horse-profile"), "Horse profile", CollectionResourceType.Horse,
                1, "service test", false);
            await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", horseId), new("horse-profile"), 1,
                CollectionReason.Initial, DateTimeOffset.UtcNow,
                attributes: new Dictionary<string, string>
                {
                    ["sourceIdentity"] = "jra-horse:1234567890",
                    ["sourceUrl"] = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002024123456/FC",
                });

            var collectionPath = Path.Combine(Path.GetFullPath(eventStorePath) + ".collection", "collection-platform.db");
            var eventBefore = await SnapshotDatabaseAsync(eventStorePath);
            var collectionBefore = await SnapshotDatabaseAsync(collectionPath);
            var service = app.Services.GetRequiredService<HorseIdentityDiagnosticService>();
            var diagnostic = await service.GetAsync(horseId, CancellationToken.None);
            Assert.IsNotNull(diagnostic);
            Assert.AreEqual(horseId, diagnostic.HorseId);

            Assert.AreEqual(string.Join("\n", eventBefore), string.Join("\n", await SnapshotDatabaseAsync(eventStorePath)));
            Assert.AreEqual(string.Join("\n", collectionBefore), string.Join("\n", await SnapshotDatabaseAsync(collectionPath)));

            await using var readonlyConnection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = eventStorePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ConnectionString);
            await readonlyConnection.OpenAsync();
            await using (var queryOnly = readonlyConnection.CreateCommand())
            {
                queryOnly.CommandText = "PRAGMA query_only = ON;";
                await queryOnly.ExecuteNonQueryAsync();
            }
            await using var writeAttempt = readonlyConnection.CreateCommand();
            writeAttempt.CommandText = "UPDATE Horses SET RegisteredName = RegisteredName WHERE HorseId = $horseId;";
            writeAttempt.Parameters.AddWithValue("$horseId", horseId);
            await Assert.ThrowsAsync<SqliteException>(async () => await writeAttempt.ExecuteNonQueryAsync());
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    [TestMethod]
    public async Task DiagnosticRead_CancellationDoesNotPoisonAdmissionAndConnectionCanBeReopened()
    {
        var root = Path.Combine(Path.GetTempPath(), "horse-identity-diagnostic-cancel", Guid.NewGuid().ToString("N"));
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
                new RegisterHorseRequest(new($"Cancellation test {Guid.NewGuid():N}", "cancel-normalized", "牡", new DateOnly(2020, 1, 2), horseId)));
            Assert.AreEqual(HttpStatusCode.Created, registration.StatusCode);

            var service = app.Services.GetRequiredService<HorseIdentityDiagnosticService>();
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetAsync(horseId, cancelled.Token));

            var afterCancellation = await service.GetAsync(horseId, CancellationToken.None);
            Assert.IsNotNull(afterCancellation);
            Assert.AreEqual(horseId, afterCancellation.HorseId);
            await using var reopened = new SqliteConnection($"Data Source={eventStorePath};Pooling=False");
            await reopened.OpenAsync();
            await using var command = reopened.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM EventEntity WHERE AggregateId = $horseId;";
            command.Parameters.AddWithValue("$horseId", horseId);
            Assert.AreEqual(1L, (long)(await command.ExecuteScalarAsync())!);
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DiagnosticRead_CancellationAfterAdmissionReleasesGateAndClosesBlockedConnection(bool lockCollectionStore)
    {
        var root = Path.Combine(Path.GetTempPath(), "horse-identity-diagnostic-admitted-cancel", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventStorePath = Path.Combine(root, "events.db");
        var collectionStorePath = Path.Combine(Path.GetFullPath(eventStorePath) + ".collection", "collection-platform.db");
        try
        {
            var (app, client) = await TestApplicationFactory.CreateAsync($"Data Source={eventStorePath};Pooling=False");
            await using var application = app;
            using var http = client;
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            var horseId = $"horse-{Guid.NewGuid():D}";
            using var registration = await http.PostAsJsonAsync("/api/horses",
                new RegisterHorseRequest(new($"Admitted cancellation {Guid.NewGuid():N}", "admitted-cancel-normalized", "牡", new DateOnly(2020, 1, 2), horseId)));
            Assert.AreEqual(HttpStatusCode.Created, registration.StatusCode);

            var eventBefore = await SnapshotDatabaseAsync(eventStorePath);
            var collectionBefore = await SnapshotDatabaseAsync(collectionStorePath);
            var lockedStorePath = lockCollectionStore ? collectionStorePath : eventStorePath;
            await using var blocker = new SqliteConnection($"Data Source={lockedStorePath};Pooling=False");
            await blocker.OpenAsync();
            if (lockCollectionStore)
            {
                await using var lockingMode = blocker.CreateCommand();
                lockingMode.CommandText = "PRAGMA locking_mode=EXCLUSIVE;";
                await lockingMode.ExecuteNonQueryAsync();
            }
            await using (var beginExclusive = blocker.CreateCommand())
            {
                beginExclusive.CommandText = "BEGIN EXCLUSIVE;";
                await beginExclusive.ExecuteNonQueryAsync();
            }

            var service = app.Services.GetRequiredService<HorseIdentityDiagnosticService>();
            using var cancelled = new CancellationTokenSource();
            var read = Task.Run(() => service.GetAsync(horseId, cancelled.Token));
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            Assert.IsFalse(read.IsCompleted, "The read must still be blocked before post-admission cancellation.");
            var admission = (SemaphoreSlim)typeof(HorseIdentityDiagnosticService)
                .GetField("Admission", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .GetValue(null)!;
            Assert.AreEqual(0, admission.CurrentCount, "The blocked read must hold diagnostic admission.");
            var cancellationStarted = Stopwatch.GetTimestamp();
            cancelled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.IsTrue(Stopwatch.GetElapsedTime(cancellationStarted) < TimeSpan.FromSeconds(3),
                "Post-admission cancellation exceeded the 3-second bound.");

            await using (var rollback = blocker.CreateCommand())
            {
                rollback.CommandText = "ROLLBACK;";
                await rollback.ExecuteNonQueryAsync();
            }

            await blocker.DisposeAsync();
            var afterCancellation = await service.GetAsync(horseId, CancellationToken.None);
            Assert.IsNotNull(afterCancellation);
            Assert.AreEqual(horseId, afterCancellation.HorseId);
            Assert.AreEqual(string.Join("\n", eventBefore), string.Join("\n", await SnapshotDatabaseAsync(eventStorePath)));
            Assert.AreEqual(string.Join("\n", collectionBefore), string.Join("\n", await SnapshotDatabaseAsync(collectionStorePath)));
            http.Dispose();
            await app.DisposeAsync();
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    [TestMethod]
    public async Task DiagnosticRead_LockedStoreMapsTo503WithoutWaitingPastDiagnosticDeadline()
    {
        var root = Path.Combine(Path.GetTempPath(), "horse-identity-diagnostic-deadline", Guid.NewGuid().ToString("N"));
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
                new RegisterHorseRequest(new($"Deadline test {Guid.NewGuid():N}", "deadline-normalized", "牡", new DateOnly(2020, 1, 2), horseId)));
            Assert.AreEqual(HttpStatusCode.Created, registration.StatusCode);

            var before = await SnapshotDatabaseAsync(eventStorePath);
            await using var blocker = new SqliteConnection($"Data Source={eventStorePath};Pooling=False");
            await blocker.OpenAsync();
            await using (var beginExclusive = blocker.CreateCommand())
            {
                beginExclusive.CommandText = "BEGIN EXCLUSIVE;";
                await beginExclusive.ExecuteNonQueryAsync();
            }

            try
            {
                var started = Stopwatch.GetTimestamp();
                using var response = await http.GetAsync($"/api/v2/admin/identity-diagnostics/horses/{horseId}")
                    .WaitAsync(TimeSpan.FromSeconds(45));
                var elapsed = Stopwatch.GetElapsedTime(started);
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                Assert.IsTrue(elapsed < TimeSpan.FromSeconds(5), $"Locked store exceeded the dedicated busy bound: {elapsed}.");
            }
            finally
            {
                await using var rollback = blocker.CreateCommand();
                rollback.CommandText = "ROLLBACK;";
                await rollback.ExecuteNonQueryAsync();
            }

            await blocker.DisposeAsync();
            Assert.AreEqual(string.Join("\n", before), string.Join("\n", await SnapshotDatabaseAsync(eventStorePath)));
            http.Dispose();
            await app.DisposeAsync();
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    [TestMethod]
    public async Task DiagnosticRead_FailsClosedForOversizedNonNullProjectionNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "horse-identity-diagnostic-size", Guid.NewGuid().ToString("N"));
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
                new RegisterHorseRequest(new($"Size test {Guid.NewGuid():N}", "size-normalized", "牡", new DateOnly(2020, 1, 2), horseId)));
            Assert.AreEqual(HttpStatusCode.Created, registration.StatusCode);
            await using (var connection = new SqliteConnection($"Data Source={eventStorePath};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var update = connection.CreateCommand();
                update.CommandText = "UPDATE Horses SET RegisteredName = $name, NormalizedName = $name WHERE HorseId = $horseId;";
                update.Parameters.AddWithValue("$name", new string('N', 65 * 1024));
                update.Parameters.AddWithValue("$horseId", horseId);
                Assert.AreEqual(1, await update.ExecuteNonQueryAsync());
            }

            var service = app.Services.GetRequiredService<HorseIdentityDiagnosticService>();
            var diagnostic = await service.GetAsync(horseId, CancellationToken.None);
            Assert.IsNotNull(diagnostic);
            Assert.AreEqual("Unsupported", diagnostic.CurrentRecordStatus);
            Assert.IsNull(diagnostic.CurrentRegisteredName);
            Assert.IsNull(diagnostic.CurrentNormalizedName);
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    private static string? ValidateMetadata(string json)
    {
        return (string?)typeof(HorseIdentityDiagnosticService)
            .GetMethod("ValidateRegisteredEventMetadata", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [json]);
    }

    private static string? ValidateShape(string json)
    {
        using var document = JsonDocument.Parse(json);
        return (string?)typeof(HorseIdentityDiagnosticService)
            .GetMethod("ValidateRegisteredEventShape", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [document.RootElement]);
    }

    private static async Task<List<string>> SnapshotDatabaseAsync(string path)
    {
        var snapshot = new List<string>();
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ConnectionString);
        await connection.OpenAsync();
        await using var schema = connection.CreateCommand();
        schema.CommandText = "SELECT type, name, tbl_name, COALESCE(sql, '') FROM sqlite_master ORDER BY type, name;";
        await using (var reader = await schema.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                snapshot.Add($"schema|{reader.GetString(0)}|{reader.GetString(1)}|{reader.GetString(2)}|{reader.GetString(3)}");
        }

        await using var tables = connection.CreateCommand();
        tables.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        var tableNames = new List<string>();
        await using (var reader = await tables.ExecuteReaderAsync())
            while (await reader.ReadAsync()) tableNames.Add(reader.GetString(0));
        foreach (var tableName in tableNames)
        {
            await using var rows = connection.CreateCommand();
            rows.CommandText = $"SELECT * FROM \"{tableName.Replace("\"", "\"\"", StringComparison.Ordinal)}\";";
            await using var reader = await rows.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var values = new string[reader.FieldCount];
                for (var index = 0; index < reader.FieldCount; index++)
                    values[index] = reader.IsDBNull(index) ? "<null>" : Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture) ?? "<null>";
                snapshot.Add($"row|{tableName}|{string.Join("|", values)}");
            }
        }
        return snapshot;
    }

    private static async Task DeleteDirectoryEventuallyAsync(string root)
    {
        for (var attempt = 0; attempt < 20 && Directory.Exists(root); attempt++)
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException) when (attempt < 19)
            {
                await Task.Delay(100);
            }
        }
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
