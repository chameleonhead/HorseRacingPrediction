namespace HorseRacingPrediction.Infrastructure.Persistence;

public sealed class SqliteMigrationOptions
{
    public bool BackupBeforeMigration { get; set; } = true;

    public string BackupDirectory { get; set; } = string.Empty;

    public int BackupRetentionCount { get; set; } = 7;
}
