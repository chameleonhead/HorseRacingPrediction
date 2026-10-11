using System.Net;
using System.Net.Http.Json;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Horses;
using HorseRacingPrediction.Contracts.Identity;
using HorseRacingPrediction.Contracts.Subjects;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityDiagnosticEndpointsTests
{
    [TestMethod]
    public async Task Diagnostic_UsesProducerProfileUrlAsCanonicalIdentityAndRedactsCredentialIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), "horse-identity-producer-diagnostic", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventStorePath = Path.Combine(root, "events.db");
        try
        {
            var (app, http) = await TestApplicationFactory.CreateAsync($"Data Source={eventStorePath};Pooling=False");
            await using var application = app;
            using var client = http;
            client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);

            const string sourceUrl = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002024123456/FC";
            var name = $"実プロファイル馬{Guid.NewGuid():N}";
            var horseId = DeterministicIdGenerator.BuildHorseId(name, sourceUrl);
            (await client.PostAsJsonAsync("/api/horses",
                new RegisterHorseRequest(new(name, name, "牡", new DateOnly(2020, 1, 2), horseId)))).EnsureSuccessStatusCode();

            var profile = new JraSubjectProfileDto("Horse", name, sourceUrl, sourceUrl,
                new() { ["生年月日"] = "2020年1月2日" }, DateTimeOffset.UtcNow);
            (await client.PutAsJsonAsync($"/api/v2/admin/subjects/Horse/{horseId}/profile",
                new PutSubjectProfileRequest("Horse", horseId, profile))).EnsureSuccessStatusCode();

            var store = app.Services.GetRequiredService<CollectionPlatformStore>();
            await store.RegisterDefinitionAsync(new("horse-profile"), "Horse profile",
                CollectionResourceType.Horse, 1, "producer profile diagnostic fixture", false);
            await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", horseId), new("horse-profile"), 1,
                CollectionReason.Initial, DateTimeOffset.UtcNow,
                attributes: new Dictionary<string, string>
                {
                    ["sourceIdentity"] = "jra-horse:2024123456",
                    ["sourceUrl"] = sourceUrl,
                });

            using (var response = await client.GetAsync($"/api/v2/admin/identity-diagnostics/horses/{horseId}"))
            {
                response.EnsureSuccessStatusCode();
                var result = (await response.Content.ReadFromJsonAsync<GetHorseIdentityDiagnosticResponse>())!;
                var profileSource = result.Diagnostic.Sources.Single(x => x.Origin == "ProfileReadModel");
                Assert.AreEqual("Supported", profileSource.Status);
                Assert.AreEqual("jra-horse:2024123456", profileSource.NormalizedSourceIdentity);
                Assert.AreEqual("Supported", profileSource.SourceUrlStatus);
                Assert.AreEqual("jra-horse:2024123456", profileSource.SourceUrlHorseIdentity);
                var expected = DeterministicIdGenerator.BuildHorseId(name, sourceUrl);
                Assert.AreEqual(expected, horseId);
                CollectionAssert.AreEquivalent(
                    new[] { expected, expected },
                    result.Diagnostic.Comparisons
                        .Where(x => x.Rule is "Source:ProfileReadModel" or "SourceUrl:ProfileReadModel")
                        .Select(x => x.CandidateHorseId).ToArray());
            }

            var credentialName = $"資格情報混入馬{Guid.NewGuid():N}";
            const string credentialUrl = "https://secret-canary@www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002024654321/FC";
            const string credentialSafeUrl = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002024654321/FC";
            var credentialHorseId = DeterministicIdGenerator.BuildHorseId(credentialName, credentialSafeUrl);
            (await client.PostAsJsonAsync("/api/horses",
                new RegisterHorseRequest(new(credentialName, credentialName, "牝", new DateOnly(2021, 3, 4), credentialHorseId)))).EnsureSuccessStatusCode();
            var credentialProfile = new JraSubjectProfileDto("Horse", credentialName, credentialUrl, credentialSafeUrl,
                new() { ["生年月日"] = "2021年3月4日" }, DateTimeOffset.UtcNow);
            (await client.PutAsJsonAsync($"/api/v2/admin/subjects/Horse/{credentialHorseId}/profile",
                new PutSubjectProfileRequest("Horse", credentialHorseId, credentialProfile))).EnsureSuccessStatusCode();

            using var credentialResponse = await client.GetAsync($"/api/v2/admin/identity-diagnostics/horses/{credentialHorseId}");
            credentialResponse.EnsureSuccessStatusCode();
            var credentialResult = (await credentialResponse.Content.ReadFromJsonAsync<GetHorseIdentityDiagnosticResponse>())!;
            var credentialSource = credentialResult.Diagnostic.Sources.Single(x => x.Origin == "ProfileReadModel");
            Assert.AreEqual("Invalid", credentialSource.Status);
            Assert.IsNull(credentialSource.NormalizedSourceIdentity);
            Assert.AreEqual("Supported", credentialSource.SourceUrlStatus);
            Assert.AreEqual("jra-horse:2024654321", credentialSource.SourceUrlHorseIdentity);
            var credentialBody = await credentialResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain("secret-canary", credentialBody);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task Diagnostic_IsApiKeyProtected_ReadOnlyAndRedactsInvalidSourceUrl()
    {
        var root = Path.Combine(Path.GetTempPath(), "horse-identity-diagnostic", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventStorePath = Path.Combine(root, "events.db");
        try
        {
            var (app, client) = await TestApplicationFactory.CreateAsync($"Data Source={eventStorePath};Pooling=False");
            await using var application = app;
            using var http = client;
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);

            var horseId = $"horse-{Guid.NewGuid():D}";
            var registeredName = $"診断対象{Guid.NewGuid():N}";
            using var registration = await http.PostAsJsonAsync("/api/horses",
                new RegisterHorseRequest(new(registeredName, "normal-key", "牡", new DateOnly(2020, 1, 2), horseId)));
            Assert.AreEqual(HttpStatusCode.Created, registration.StatusCode);
            var sameNameHorseId = $"horse-{Guid.NewGuid():D}";
            using var sameNameRegistration = await http.PostAsJsonAsync("/api/horses",
                new RegisterHorseRequest(new(registeredName, "normal-key", "牡", new DateOnly(2020, 1, 2), sameNameHorseId)));
            Assert.AreEqual(HttpStatusCode.Created, sameNameRegistration.StatusCode);

            var emittedMetadata = await ReadEventMetadataAsync(eventStorePath, horseId);
            Assert.AreEqual("HorseRegistered", emittedMetadata["event_name"]?.GetValue<string>());
            Assert.AreEqual("1", emittedMetadata["event_version"]?.GetValue<string>());
            Assert.AreEqual("HorseAggregate", emittedMetadata["aggregate_name"]?.GetValue<string>());

            var store = app.Services.GetRequiredService<CollectionPlatformStore>();
            await store.RegisterDefinitionAsync(new("horse-profile"), "Horse profile",
                CollectionResourceType.Horse, 1, "diagnostic fixture", false);
            await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", horseId), new("horse-profile"), 1,
                CollectionReason.Initial, DateTimeOffset.UtcNow,
                attributes: new Dictionary<string, string>
                {
                    ["sourceIdentity"] = "jra-horse:1234567890",
                    ["sourceUrl"] = "https://private-token@www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dudxx1234567890",
                });

            var beforeEvents = await CountEventsAsync(eventStorePath);
            var beforeEventSchema = await CountSchemaObjectsAsync(eventStorePath);
            var collectionPath = Path.Combine(Path.GetFullPath(eventStorePath) + ".collection", "collection-platform.db");
            var beforeResources = await CountResourcesAsync(collectionPath);
            var beforeCollectionSchema = await CountSchemaObjectsAsync(collectionPath);
            var beforeTasks = (await store.GetTasksAsync(limit: 100)).Count;
            var diagnosticPath = $"/api/v2/admin/identity-diagnostics/horses/{horseId}";
            using (var unauthenticatedClient = app.GetTestClient())
            using (var unauthenticated = await unauthenticatedClient.GetAsync(diagnosticPath))
                Assert.AreEqual(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

            var admission = (SemaphoreSlim)typeof(HorseIdentityDiagnosticService)
                .GetField("Admission", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .GetValue(null)!;
            await admission.WaitAsync();
            try
            {
                using var busy = await http.GetAsync(diagnosticPath);
                Assert.AreEqual(HttpStatusCode.TooManyRequests, busy.StatusCode);
            }
            finally
            {
                admission.Release();
            }

            using var response = await http.GetAsync(diagnosticPath);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<GetHorseIdentityDiagnosticResponse>();
            Assert.IsNotNull(result);
            Assert.AreEqual(horseId, result.Diagnostic.HorseId);
            Assert.AreEqual(registeredName, result.Diagnostic.InitialRegisteredName,
                $"Registration status: {result.Diagnostic.RegistrationStatus}; blockers: {string.Join(',', result.Diagnostic.EvidenceBlockers)}");
            Assert.AreEqual("Supported", result.Diagnostic.RegistrationStatus);
            Assert.AreEqual("normal-key", result.Diagnostic.InitialNormalizedName);
            var collectionSource = result.Diagnostic.Sources.Single(x => x.Origin == "CollectionResource");
            Assert.AreEqual("Supported", collectionSource.Status);
            Assert.AreEqual("jra-horse:1234567890", collectionSource.NormalizedSourceIdentity);
            Assert.AreEqual("Invalid", collectionSource.SourceUrlStatus);
            Assert.IsNull(collectionSource.SourceUrlHorseIdentity);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("private-token", body);
            Assert.DoesNotContain("accessU.html", body);
            var currentNameCandidate = result.Diagnostic.Comparisons.Single(x => x.Rule == "CurrentNameOnly").CandidateHorseId;
            using (var sameNameResponse = await http.GetAsync($"/api/v2/admin/identity-diagnostics/horses/{sameNameHorseId}"))
            {
                var sameNameResult = (await sameNameResponse.Content.ReadFromJsonAsync<GetHorseIdentityDiagnosticResponse>())!;
                Assert.AreEqual(currentNameCandidate,
                    sameNameResult.Diagnostic.Comparisons.Single(x => x.Rule == "CurrentNameOnly").CandidateHorseId);
                Assert.AreNotEqual(horseId, currentNameCandidate,
                    "A name-derived candidate must not silently adopt either same-name aggregate ID.");
            }
            Assert.AreEqual(beforeEvents, await CountEventsAsync(eventStorePath));
            Assert.AreEqual(beforeEventSchema, await CountSchemaObjectsAsync(eventStorePath));
            Assert.AreEqual(beforeResources, await CountResourcesAsync(collectionPath));
            Assert.AreEqual(beforeCollectionSchema, await CountSchemaObjectsAsync(collectionPath));
            Assert.AreEqual(beforeTasks, (await store.GetTasksAsync(limit: 100)).Count);

            await ReplaceEventDataAsync(eventStorePath, horseId, static data => data["NormalizedName"] = null);
            using (var missingNormalized = await http.GetAsync(diagnosticPath))
            {
                var missingResult = (await missingNormalized.Content.ReadFromJsonAsync<GetHorseIdentityDiagnosticResponse>())!;
                Assert.AreEqual("Missing", missingResult.Diagnostic.RegistrationStatus);
                Assert.IsTrue(missingResult.Diagnostic.EvidenceBlockers.Contains("MissingNormalizedName"));
                Assert.AreEqual("InitialRegisteredNameFallback",
                    missingResult.Diagnostic.Comparisons.Single(x => x.Rule == "LegacyNormalizedName").InputField);
            }

            await ReplaceEventDataAsync(eventStorePath, horseId, static data => data["NormalizedName"] = string.Empty);
            using (var emptyNormalized = await http.GetAsync(diagnosticPath))
            {
                var emptyResult = (await emptyNormalized.Content.ReadFromJsonAsync<GetHorseIdentityDiagnosticResponse>())!;
                Assert.AreEqual("Supported", emptyResult.Diagnostic.RegistrationStatus);
                var comparison = emptyResult.Diagnostic.Comparisons.Single(x => x.Rule == "LegacyNormalizedName");
                Assert.AreEqual("InitialNormalizedName", comparison.InputField);
                Assert.AreEqual("Invalid", comparison.EvidenceStatus);
            }

            await SetCollectionSourceUrlAsync(collectionPath, horseId,
                "https://www.jra.go.jp/JRADB/accessU.html?CNAME=unrecognized-source-text");
            using (var invalidSource = await http.GetAsync(diagnosticPath))
            {
                var invalidResult = (await invalidSource.Content.ReadFromJsonAsync<GetHorseIdentityDiagnosticResponse>())!;
                var source = invalidResult.Diagnostic.Sources.Single(x => x.Origin == "CollectionResource");
                Assert.AreEqual("Invalid", source.SourceUrlStatus);
                Assert.IsNull(source.SourceUrlHorseIdentity);
                Assert.DoesNotContain("unrecognized-source-text", await invalidSource.Content.ReadAsStringAsync());
            }

            await SetCollectionSourceUrlAsync(collectionPath, horseId,
                "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002024999999/FC");
            using (var conflictingSource = await http.GetAsync(diagnosticPath))
            {
                var conflictingResult = (await conflictingSource.Content.ReadFromJsonAsync<GetHorseIdentityDiagnosticResponse>())!;
                var source = conflictingResult.Diagnostic.Sources.Single(x => x.Origin == "CollectionResource");
                Assert.AreEqual("Conflicting", source.Status,
                    $"identity={source.NormalizedSourceIdentity}, url={source.SourceUrlHorseIdentity}, urlStatus={source.SourceUrlStatus}");
                Assert.AreEqual("jra-horse:1234567890", source.NormalizedSourceIdentity);
                Assert.AreEqual("jra-horse:2024999999", source.SourceUrlHorseIdentity);
                Assert.IsTrue(conflictingResult.Diagnostic.Comparisons.Any(x => x.Rule == "SourceUrl:CollectionResource"));
            }

            await ReplaceEventMetadataAsync(eventStorePath, horseId, static metadata => metadata["event_version"] = 99);
            using (var unsupported = await http.GetAsync(diagnosticPath))
            {
                Assert.AreEqual(HttpStatusCode.OK, unsupported.StatusCode);
                var unsupportedResult = (await unsupported.Content.ReadFromJsonAsync<GetHorseIdentityDiagnosticResponse>())!;
                Assert.AreEqual("Unsupported", unsupportedResult.Diagnostic.RegistrationStatus);
                Assert.IsTrue(unsupportedResult.Diagnostic.EvidenceBlockers.Contains("UnsupportedEventVersion"));
                Assert.IsNull(unsupportedResult.Diagnostic.InitialRegisteredName);
            }

            await ReplaceEventMetadataAsync(eventStorePath, horseId, static metadata => metadata["event_version"] = "1");
            await ReplaceEventDataRawAsync(eventStorePath, horseId, "{not-json");
            using (var malformed = await http.GetAsync(diagnosticPath))
            {
                var malformedResult = (await malformed.Content.ReadFromJsonAsync<GetHorseIdentityDiagnosticResponse>())!;
                Assert.AreEqual("Unsupported", malformedResult.Diagnostic.RegistrationStatus);
                Assert.IsTrue(malformedResult.Diagnostic.EvidenceBlockers.Contains("InvalidEventJson"));
                Assert.IsNull(malformedResult.Diagnostic.InitialRegisteredName);
            }

            var nestedEvent = "{}";
            for (var depth = 0; depth < 17; depth++) nestedEvent = $"{{\"nested\":{nestedEvent}}}";
            await ReplaceEventDataRawAsync(eventStorePath, horseId, nestedEvent);
            using (var tooDeep = await http.GetAsync(diagnosticPath))
            {
                var tooDeepResult = (await tooDeep.Content.ReadFromJsonAsync<GetHorseIdentityDiagnosticResponse>())!;
                Assert.AreEqual("Unsupported", tooDeepResult.Diagnostic.RegistrationStatus);
                Assert.IsTrue(tooDeepResult.Diagnostic.EvidenceBlockers.Contains("InvalidEventJson"));
            }

            await ReplaceEventDataRawAsync(eventStorePath, horseId, new string('x', 65 * 1024));
            using (var oversized = await http.GetAsync(diagnosticPath))
            {
                var oversizedResult = (await oversized.Content.ReadFromJsonAsync<GetHorseIdentityDiagnosticResponse>())!;
                Assert.AreEqual("Unsupported", oversizedResult.Diagnostic.RegistrationStatus);
                Assert.IsTrue(oversizedResult.Diagnostic.EvidenceBlockers.Contains("PayloadTooLarge"));
            }

            await DropEventSequenceUniquenessAsync(eventStorePath);
            await InsertDuplicateSequenceOneAsync(eventStorePath, horseId);
            using (var duplicate = await http.GetAsync(diagnosticPath))
            {
                var duplicateResult = (await duplicate.Content.ReadFromJsonAsync<GetHorseIdentityDiagnosticResponse>())!;
                Assert.AreEqual("Unsupported", duplicateResult.Diagnostic.RegistrationStatus);
                Assert.IsTrue(duplicateResult.Diagnostic.EvidenceBlockers.Contains("DuplicateSequenceOne"));
            }

            using var invalid = await http.GetAsync("/api/v2/admin/identity-diagnostics/horses/horse-not-a-guid");
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
            var uppercaseHorseId = $"horse-{Guid.NewGuid():D}";
            using var nonCanonical = await http.GetAsync($"/api/v2/admin/identity-diagnostics/horses/horse-{uppercaseHorseId[6..].ToUpperInvariant()}");
            Assert.AreEqual(HttpStatusCode.BadRequest, nonCanonical.StatusCode);
            using var missing = await http.GetAsync($"/api/v2/admin/identity-diagnostics/horses/horse-{Guid.NewGuid():D}");
            Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<long> CountEventsAsync(string path)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM EventEntity;";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> CountResourcesAsync(string path)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM collection_resources;";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> CountSchemaObjectsAsync(string path)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type IN ('table','index','trigger','view');";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ReplaceEventMetadataAsync(string path, string horseId,
        Action<System.Text.Json.Nodes.JsonObject> change)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Metadata FROM EventEntity WHERE AggregateId = $horseId AND AggregateSequenceNumber = 1 LIMIT 1;";
        command.Parameters.AddWithValue("$horseId", horseId);
        var metadataJson = (string)(await command.ExecuteScalarAsync())!;
        var metadata = System.Text.Json.Nodes.JsonNode.Parse(metadataJson)!.AsObject();
        change(metadata);
        await using var update = connection.CreateCommand();
        update.CommandText = "UPDATE EventEntity SET Metadata = $metadata WHERE AggregateId = $horseId AND AggregateSequenceNumber = 1;";
        update.Parameters.AddWithValue("$metadata", metadata.ToJsonString());
        update.Parameters.AddWithValue("$horseId", horseId);
        await update.ExecuteNonQueryAsync();
    }

    private static async Task ReplaceEventDataAsync(string path, string horseId,
        Action<System.Text.Json.Nodes.JsonObject> change)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Data FROM EventEntity WHERE AggregateId = $horseId AND AggregateSequenceNumber = 1 LIMIT 1;";
        command.Parameters.AddWithValue("$horseId", horseId);
        var data = System.Text.Json.Nodes.JsonNode.Parse((string)(await command.ExecuteScalarAsync())!)!.AsObject();
        change(data);
        await using var update = connection.CreateCommand();
        update.CommandText = "UPDATE EventEntity SET Data = $data WHERE AggregateId = $horseId AND AggregateSequenceNumber = 1;";
        update.Parameters.AddWithValue("$data", data.ToJsonString());
        update.Parameters.AddWithValue("$horseId", horseId);
        await update.ExecuteNonQueryAsync();
    }

    private static async Task ReplaceEventDataRawAsync(string path, string horseId, string data)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var update = connection.CreateCommand();
        update.CommandText = "UPDATE EventEntity SET Data = $data WHERE AggregateId = $horseId AND AggregateSequenceNumber = 1;";
        update.Parameters.AddWithValue("$data", data);
        update.Parameters.AddWithValue("$horseId", horseId);
        Assert.AreEqual(1, await update.ExecuteNonQueryAsync());
    }

    private static async Task InsertDuplicateSequenceOneAsync(string path, string horseId)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO EventEntity(AggregateId, AggregateName, AggregateSequenceNumber, BatchId, Data, Metadata)
            SELECT AggregateId, AggregateName, AggregateSequenceNumber, $batchId, Data, Metadata
            FROM EventEntity
            WHERE AggregateId = $horseId AND AggregateSequenceNumber = 1
            ORDER BY GlobalSequenceNumber
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$batchId", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$horseId", horseId);
        Assert.AreEqual(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task DropEventSequenceUniquenessAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var lookup = connection.CreateCommand();
        lookup.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'EventEntity' AND sql LIKE '%AggregateSequenceNumber%';";
        var indexes = new List<string>();
        await using (var reader = await lookup.ExecuteReaderAsync())
            while (await reader.ReadAsync()) indexes.Add(reader.GetString(0));
        foreach (var indexName in indexes)
        {
            await using var drop = connection.CreateCommand();
            drop.CommandText = $"DROP INDEX \"{indexName.Replace("\"", "\"\"")}\";";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task<System.Text.Json.Nodes.JsonObject> ReadEventMetadataAsync(string path, string horseId)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
        }.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Metadata FROM EventEntity WHERE AggregateId = $horseId AND AggregateSequenceNumber = 1 LIMIT 1;";
        command.Parameters.AddWithValue("$horseId", horseId);
        return System.Text.Json.Nodes.JsonNode.Parse((string)(await command.ExecuteScalarAsync())!)!.AsObject();
    }

    private static async Task SetCollectionSourceUrlAsync(string path, string horseId, string sourceUrl)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE collection_resources SET AttributesJson = json_set(AttributesJson, '$.sourceUrl', $sourceUrl) WHERE ResourceId = $horseId;";
        command.Parameters.AddWithValue("$sourceUrl", sourceUrl);
        command.Parameters.AddWithValue("$horseId", horseId);
        Assert.AreEqual(1, await command.ExecuteNonQueryAsync());
    }
}
