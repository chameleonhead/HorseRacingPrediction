using System.Text.Json;
using HorseRacingPrediction.Contracts.Time;
using Microsoft.Data.Sqlite;

namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public sealed record LocalCollectionQueueMessage(long MessageId, CollectionDispatchEnvelope? Envelope,
    string ReceiptHandle, int ReceiveCount, CollectionWakeSignal? Wake = null);

public sealed class LocalCollectionQueue
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string _connectionString;

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JstStoredDateTimeOffsetJsonConverter());
        return options;
    }

    public LocalCollectionQueue(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connectionString = $"Data Source={fullPath};Default Timeout=30;Pooling=False";
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS local_collection_messages (
                message_id INTEGER PRIMARY KEY AUTOINCREMENT,
                body TEXT NOT NULL,
                visible_at TEXT NOT NULL,
                receipt_handle TEXT NULL,
                receive_count INTEGER NOT NULL DEFAULT 0,
                dead_letter INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
        command.CommandText = """
            UPDATE local_collection_messages
            SET visible_at = CASE WHEN substr(visible_at, -1, 1) = 'Z' OR instr(substr(visible_at, 12), '+') > 0 OR instr(substr(visible_at, 12), '-') > 0
                                  THEN strftime('%Y-%m-%d %H:%M:%f', visible_at, '+9 hours') ELSE visible_at END,
                created_at = CASE WHEN substr(created_at, -1, 1) = 'Z' OR instr(substr(created_at, 12), '+') > 0 OR instr(substr(created_at, 12), '-') > 0
                                  THEN strftime('%Y-%m-%d %H:%M:%f', created_at, '+9 hours') ELSE created_at END;
            """;
        command.ExecuteNonQuery();
    }

    public async Task<long> SendAsync(CollectionDispatchEnvelope envelope, CancellationToken token = default)
        => await SendBodyAsync(JsonSerializer.Serialize(envelope, JsonOptions), token).ConfigureAwait(false);

    public async Task<long> SendWakeAsync(CollectionWakeSignal wake, CancellationToken token = default)
        => await SendBodyAsync(JsonSerializer.Serialize(wake, JsonOptions), token).ConfigureAwait(false);

    private async Task<long> SendBodyAsync(string body, CancellationToken token)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO local_collection_messages(body, visible_at, created_at) VALUES($body,$now,$now)";
        command.Parameters.AddWithValue("$body", body);
        command.Parameters.AddWithValue("$now", JstTime.ToDatabaseString(JstTime.Now()));
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        command.CommandText = "SELECT last_insert_rowid();";
        command.Parameters.Clear();
        return Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
    }

    public async Task<LocalCollectionQueueMessage?> ReceiveAsync(TimeSpan visibilityTimeout,
        CancellationToken token = default)
    {
        await using var connection = Open();
        await using var transaction = await connection.BeginTransactionAsync(token).ConfigureAwait(false);
        await using var select = connection.CreateCommand();
        select.Transaction = (SqliteTransaction)transaction;
        select.CommandText = """
            SELECT message_id, body, receive_count FROM local_collection_messages
            WHERE dead_letter=0 AND visible_at <= $now ORDER BY message_id LIMIT 1
            """;
        select.Parameters.AddWithValue("$now", JstTime.ToDatabaseString(JstTime.Now()));
        await using var reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
        var id = reader.GetInt64(0);
        var body = reader.GetString(1);
        var count = reader.GetInt32(2) + 1;
        await reader.DisposeAsync().ConfigureAwait(false);
        var receipt = Guid.NewGuid().ToString("N");
        await using var update = connection.CreateCommand();
        update.Transaction = (SqliteTransaction)transaction;
        update.CommandText = "UPDATE local_collection_messages SET visible_at=$visible, receipt_handle=$receipt, receive_count=$count WHERE message_id=$id";
        update.Parameters.AddWithValue("$visible", JstTime.ToDatabaseString(JstTime.Now().Add(visibilityTimeout)));
        update.Parameters.AddWithValue("$receipt", receipt);
        update.Parameters.AddWithValue("$count", count);
        update.Parameters.AddWithValue("$id", id);
        await update.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
        CollectionWakeSignal? wake = null;
        CollectionDispatchEnvelope? envelope = null;
        try { wake = JsonSerializer.Deserialize<CollectionWakeSignal>(body, JsonOptions); }
        catch (JsonException) { }
        if (wake is null || wake.WakeId == Guid.Empty)
            envelope = JsonSerializer.Deserialize<CollectionDispatchEnvelope>(body, JsonOptions);
        return new(id, envelope, receipt, count, wake);
    }

    public Task AcknowledgeAsync(string receiptHandle, CancellationToken token = default)
        => ExecuteAsync("DELETE FROM local_collection_messages WHERE receipt_handle=$receipt", receiptHandle, token);

    public Task ReleaseAsync(string receiptHandle, int maxReceiveCount = 3,
        CancellationToken token = default)
        => ExecuteAsync("UPDATE local_collection_messages SET visible_at=$now, receipt_handle=NULL, dead_letter=CASE WHEN receive_count >= $max THEN 1 ELSE 0 END WHERE receipt_handle=$receipt",
            receiptHandle, token, maxReceiveCount);

    public async Task<(long Visible, long NotVisible, long DeadLetter)> GetDepthAsync(CancellationToken token = default)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SUM(CASE WHEN dead_letter=0 AND visible_at <= $now THEN 1 ELSE 0 END), SUM(CASE WHEN dead_letter=0 AND visible_at > $now THEN 1 ELSE 0 END), SUM(dead_letter) FROM local_collection_messages";
        command.Parameters.AddWithValue("$now", JstTime.ToDatabaseString(JstTime.Now()));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        await reader.ReadAsync(token).ConfigureAwait(false);
        return (reader.IsDBNull(0) ? 0 : reader.GetInt64(0), reader.IsDBNull(1) ? 0 : reader.GetInt64(1), reader.IsDBNull(2) ? 0 : reader.GetInt64(2));
    }

    private async Task ExecuteAsync(string sql, string receipt, CancellationToken token, int max = 3)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$receipt", receipt);
        command.Parameters.AddWithValue("$now", JstTime.ToDatabaseString(JstTime.Now()));
        command.Parameters.AddWithValue("$max", max);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private SqliteConnection Open() { var connection = new SqliteConnection(_connectionString); connection.Open(); return connection; }
}
