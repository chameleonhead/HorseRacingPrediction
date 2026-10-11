using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public sealed partial class CollectionPlatformStore
{
    private const int HorseIdentityDiagnosticMaxPayloadBytes = 64 * 1024;
    private const int HorseIdentityDiagnosticSourceLimit = 16;

    public async Task<CollectionHorseIdentitySources> GetHorseIdentitySourcesReadOnlyAsync(
        string horseId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(horseId);

        string connectionString;
        await using (var optionsContext = new CollectionPlatformDbContext(_dbOptions))
            connectionString = optionsContext.Database.GetConnectionString()
                ?? throw new InvalidOperationException("Collection database connection is unavailable.");

        var connectionOptions = new SqliteConnectionStringBuilder(connectionString)
        {
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 1,
        };
        await using var connection = new SqliteConnection(connectionOptions.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA query_only = ON;";
            pragma.CommandTimeout = 1;
            await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await using var transaction = connection.BeginTransaction(deferred: true);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 1;
        command.Transaction = transaction;
        command.CommandText = """
            SELECT ResourcePk,
                   CASE WHEN length(CAST(AttributesJson AS BLOB)) <= $maxBytes THEN AttributesJson ELSE NULL END,
                   length(CAST(AttributesJson AS BLOB))
            FROM collection_resources
            WHERE Type = 'Horse' AND Provider = 'JRA' AND ResourceId = $horseId
            ORDER BY ResourcePk
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$maxBytes", HorseIdentityDiagnosticMaxPayloadBytes);
        command.Parameters.AddWithValue("$horseId", horseId);
        command.Parameters.AddWithValue("$limit", HorseIdentityDiagnosticSourceLimit + 1);

        var rows = new List<CollectionHorseIdentitySourceRow>(HorseIdentityDiagnosticSourceLimit + 1);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var resourcePk = reader.GetInt64(0);
                var payloadBytes = reader.GetInt64(2);
                if (reader.IsDBNull(1))
                {
                    rows.Add(new(resourcePk, null, null, payloadBytes > HorseIdentityDiagnosticMaxPayloadBytes
                        ? "PayloadTooLarge" : "MissingAttributes"));
                    continue;
                }

                var attributesJson = reader.GetString(1);
                try
                {
                    using var document = System.Text.Json.JsonDocument.Parse(attributesJson,
                        new System.Text.Json.JsonDocumentOptions { MaxDepth = 16 });
                    if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                    {
                        rows.Add(new(resourcePk, null, null, "InvalidAttributes"));
                        continue;
                    }

                    rows.Add(new(resourcePk,
                        GetString(document.RootElement, "sourceIdentity"),
                        GetString(document.RootElement, "sourceUrl"),
                        "Read"));
                }
                catch (System.Text.Json.JsonException)
                {
                    rows.Add(new(resourcePk, null, null, "InvalidAttributes"));
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new(rows.Take(HorseIdentityDiagnosticSourceLimit).ToArray(),
            rows.Count > HorseIdentityDiagnosticSourceLimit);
    }

    private static string? GetString(System.Text.Json.JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase)) continue;
            return property.Value.ValueKind == System.Text.Json.JsonValueKind.String
                ? property.Value.GetString() : null;
        }
        return null;
    }
}

public sealed record CollectionHorseIdentitySources(
    IReadOnlyList<CollectionHorseIdentitySourceRow> Rows,
    bool Truncated);

public sealed record CollectionHorseIdentitySourceRow(
    long ResourcePk,
    string? SourceIdentity,
    string? SourceUrl,
    string Status);
