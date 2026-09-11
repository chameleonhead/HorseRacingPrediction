using System.Text.Json;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.CollectionInitializer;

public static class CollectionInitializerCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error,
        CancellationToken cancellationToken = default)
    {
        var arguments = args.Select((value, index) => (value, index))
            .ToDictionary(x => x.value, x => x.index, StringComparer.Ordinal);
        if (!arguments.TryGetValue("--domain-db", out var domainIndex) || domainIndex + 1 >= args.Length
            || !arguments.TryGetValue("--state-dir", out var stateIndex) || stateIndex + 1 >= args.Length)
        {
            await error.WriteLineAsync("Usage: --domain-db <eventstore.db> --state-dir <directory> [--execute]");
            return 2;
        }

        await error.WriteLineAsync("Boundary: Domain Data and source citations are read-only and retained. " +
            "Only the new CollectionPlatform database may be written. Legacy job data and old SQS/DLQ are not read or deleted by this tool.");
        var seeds = await new DomainCollectionSeedReader(args[domainIndex + 1])
            .ReadAsync(DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        CollectionInitializationReport report;
        if (!arguments.ContainsKey("--execute"))
        {
            var stateDatabasePath = Path.Combine(Path.GetFullPath(args[stateIndex + 1]), "collection-platform.db");
            report = await PreviewReadOnlyAsync(stateDatabasePath, seeds, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            {
                StateDirectory = args[stateIndex + 1]
            }));
            await RegisterDefinitionsAsync(store, cancellationToken).ConfigureAwait(false);
            report = await store.InitializeFromDomainDataAsync(seeds, false, cancellationToken).ConfigureAwait(false);
        }
        await output.WriteLineAsync(JsonSerializer.Serialize(report,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        return 0;
    }

    private static async Task<CollectionInitializationReport> PreviewReadOnlyAsync(string databasePath,
        IReadOnlyCollection<CollectionInitializationSeed> seeds, CancellationToken cancellationToken)
    {
        var normalized = seeds.Select(x => x with { Resource = x.Resource.Normalize() })
            .GroupBy(x => new { x.Resource, x.Definition })
            .Select(x => x.OrderByDescending(y => y.CollectedAt).First()).ToList();
        var months = normalized.Where(x => x.EffectiveDate.HasValue)
            .Select(x => $"{x.EffectiveDate!.Value:yyyy-MM}").Distinct().Order().ToList();
        if (!File.Exists(databasePath))
            return new(true, normalized.Count, normalized.Select(x => x.Resource).Distinct().Count(),
                normalized.Count, normalized.Count(x => x.SourceUrl is not null), months);

        var resourcesAdded = 0;
        var statesAdded = 0;
        var locationsAdded = 0;
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        foreach (var seed in normalized)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT r.ResourcePk,
                       EXISTS(SELECT 1 FROM collection_states s WHERE s.ResourcePk=r.ResourcePk AND s.DefinitionId=$definition),
                       EXISTS(SELECT 1 FROM resource_locations l WHERE l.ResourcePk=r.ResourcePk AND l.DefinitionId=$definition AND l.Url=$url)
                FROM collection_resources r
                WHERE r.Type=$type AND r.Provider=$provider AND r.ResourceId=$resourceId;
                """;
            command.Parameters.AddWithValue("$definition", seed.Definition.Value);
            command.Parameters.AddWithValue("$type", seed.Resource.Type.ToString());
            command.Parameters.AddWithValue("$provider", seed.Resource.Provider);
            command.Parameters.AddWithValue("$resourceId", seed.Resource.Id);
            command.Parameters.AddWithValue("$url", seed.SourceUrl?.AbsoluteUri ?? string.Empty);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                resourcesAdded++;
                statesAdded++;
                if (seed.SourceUrl is not null) locationsAdded++;
            }
            else
            {
                if (reader.GetInt64(1) == 0) statesAdded++;
                if (seed.SourceUrl is not null && reader.GetInt64(2) == 0) locationsAdded++;
            }
        }
        return new(true, normalized.Count, resourcesAdded, statesAdded, locationsAdded, months);
    }

    private static async Task RegisterDefinitionsAsync(CollectionPlatformStore store, CancellationToken token)
    {
        await store.RegisterDefinitionAsync(new("race-card"), "Race card", ResourceType.RaceCard, 1, "Initial", false, token);
        await store.RegisterDefinitionAsync(new("race-result"), "Race result", ResourceType.RaceResult, 1, "Initial", false, token);
        await store.RegisterDefinitionAsync(new("horse-profile"), "Horse profile", ResourceType.Horse, 1, "Initial", false, token);
        await store.RegisterDefinitionAsync(new("trainer-profile"), "Trainer profile", ResourceType.Trainer, 1, "Initial", false, token);
    }
}
