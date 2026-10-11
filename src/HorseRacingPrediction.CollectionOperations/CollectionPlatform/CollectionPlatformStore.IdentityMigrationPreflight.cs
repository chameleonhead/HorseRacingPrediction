using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public sealed partial class CollectionPlatformStore
{
    /// <summary>
    /// Resolves the configured file for the read-only identity preflight without creating a
    /// CollectionPlatformStore (whose constructor owns schema migration).
    /// </summary>
    public static string ResolveIdentityMigrationPreflightDatabasePath(
        IOptions<CollectionPlatformOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;
        var directory = Path.GetFullPath(string.IsNullOrWhiteSpace(value.StateDirectory)
            ? "collection-platform-state" : value.StateDirectory);
        return Path.Combine(directory, string.IsNullOrWhiteSpace(value.DatabaseFileName)
            ? "collection-platform.db" : value.DatabaseFileName);
    }
}
