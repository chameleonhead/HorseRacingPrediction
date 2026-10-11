using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Collection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class HorseIdentityDiagnosticReadTests
{
    [TestMethod]
    public async Task ReadOnlySourceReader_ReturnsCanonicalFieldsAndMarksOversizedPayloadWithoutMaterializingIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "horse-identity-collection-diagnostic", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "collection.db");
        try
        {
            var options = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={path};Pooling=False").Options;
            var store = new CollectionPlatformStore(options);
            var horseId = $"horse-{Guid.NewGuid():D}";
            await store.RegisterDefinitionAsync(new("horse-profile"), "Horse profile",
                CollectionResourceType.Horse, 1, "diagnostic fixture", false);
            await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", horseId), new("horse-profile"), 1,
                CollectionReason.Initial, DateTimeOffset.UtcNow,
                attributes: new Dictionary<string, string>
                {
                    ["sourceIdentity"] = "jra-horse:1234567890",
                    ["sourceUrl"] = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dudxx1234567890",
                });

            var before = await CountResourcesAsync(path);
            var read = await store.GetHorseIdentitySourcesReadOnlyAsync(horseId);
            Assert.AreEqual(1, read.Rows.Count);
            Assert.IsFalse(read.Truncated);
            Assert.AreEqual("jra-horse:1234567890", read.Rows[0].SourceIdentity);
            Assert.AreEqual("Read", read.Rows[0].Status);
            Assert.AreEqual(before, await CountResourcesAsync(path));

            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "UPDATE collection_resources SET AttributesJson = $value WHERE ResourceId = $horseId;";
                command.Parameters.AddWithValue("$value", new string('x', 65 * 1024));
                command.Parameters.AddWithValue("$horseId", horseId);
                Assert.AreEqual(1, await command.ExecuteNonQueryAsync());
            }

            var oversized = await store.GetHorseIdentitySourcesReadOnlyAsync(horseId);
            Assert.AreEqual("PayloadTooLarge", oversized.Rows.Single().Status);
            Assert.IsNull(oversized.Rows.Single().SourceIdentity);
            Assert.IsNull(oversized.Rows.Single().SourceUrl);

            await SetAttributesAsync(path, horseId, "{not-json");
            var malformed = await store.GetHorseIdentitySourcesReadOnlyAsync(horseId);
            Assert.AreEqual("InvalidAttributes", malformed.Rows.Single().Status);

            var nested = "{}";
            for (var depth = 0; depth < 17; depth++) nested = $"{{\"nested\":{nested}}}";
            await SetAttributesAsync(path, horseId, nested);
            var tooDeep = await store.GetHorseIdentitySourcesReadOnlyAsync(horseId);
            Assert.AreEqual("InvalidAttributes", tooDeep.Rows.Single().Status);

            await SetAttributesAsync(path, horseId, "{\"sourceIdentity\":\"jra-horse:1234567890\"}");
            await DropResourceUniquenessAsync(path);
            await InsertDuplicateResourcesAsync(path, horseId, 17);
            var schemaBeforeLimitedRead = await CountSchemaObjectsAsync(path);
            var limited = await store.GetHorseIdentitySourcesReadOnlyAsync(horseId);
            Assert.AreEqual(16, limited.Rows.Count);
            Assert.IsTrue(limited.Truncated);
            Assert.AreEqual(schemaBeforeLimitedRead, await CountSchemaObjectsAsync(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
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
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type IN ('table','index','trigger','view');";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task SetAttributesAsync(string path, string horseId, string attributes)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE collection_resources SET AttributesJson = $value WHERE ResourceId = $horseId;";
        command.Parameters.AddWithValue("$value", attributes);
        command.Parameters.AddWithValue("$horseId", horseId);
        Assert.IsGreaterThan(0, await command.ExecuteNonQueryAsync());
    }

    private static async Task DropResourceUniquenessAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var lookup = connection.CreateCommand();
        lookup.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'collection_resources' AND sql LIKE '%UNIQUE%';";
        var indexName = (string?)await lookup.ExecuteScalarAsync();
        Assert.IsFalse(string.IsNullOrWhiteSpace(indexName));
        await using var drop = connection.CreateCommand();
        drop.CommandText = $"DROP INDEX \"{indexName!.Replace("\"", "\"\"")}\";";
        await drop.ExecuteNonQueryAsync();
    }

    private static async Task InsertDuplicateResourcesAsync(string path, string horseId, int count)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        for (var index = 0; index < count; index++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO collection_resources(ResourcePk, Type, Provider, ResourceId, EffectiveDate, AttributesJson, CreatedAt)
                VALUES ($pk, 'Horse', 'JRA', $resourceId, NULL, $attributes, $createdAt);
                """;
            command.Parameters.AddWithValue("$pk", 10_000 + index);
            command.Parameters.AddWithValue("$resourceId", horseId);
            command.Parameters.AddWithValue("$attributes", "{\"sourceIdentity\":\"jra-horse:1234567890\"}");
            command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }
    }
}
