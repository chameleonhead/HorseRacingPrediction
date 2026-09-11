using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public sealed record LocalCollectionQueueMessage(long MessageId, CollectionTaskNotification Notification,
    string ReceiptHandle, int ReceiveCount);

public sealed class LocalCollectionQueue
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;

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
    }

    public async Task SendAsync(CollectionTaskNotification notification, CancellationToken token = default)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO local_collection_messages(body, visible_at, created_at) VALUES($body,$now,$now)";
        command.Parameters.AddWithValue("$body", JsonSerializer.Serialize(notification, JsonOptions));
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
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
        select.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
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
        update.Parameters.AddWithValue("$visible", DateTimeOffset.UtcNow.Add(visibilityTimeout).ToString("O"));
        update.Parameters.AddWithValue("$receipt", receipt);
        update.Parameters.AddWithValue("$count", count);
        update.Parameters.AddWithValue("$id", id);
        await update.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
        return new(id, JsonSerializer.Deserialize<CollectionTaskNotification>(body, JsonOptions)!, receipt, count);
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
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
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
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$max", max);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private SqliteConnection Open() { var connection = new SqliteConnection(_connectionString); connection.Open(); return connection; }
}
