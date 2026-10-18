using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.PredictionScheduling;
using Microsoft.Data.Sqlite;

namespace HorseRacingPrediction.Api.IdentityMigration;

internal sealed record HorseIdentityMigrationConfiguredSources(
    string? EventStorePath,
    string? CollectionPlatformPath,
    string? PredictionSchedulePath,
    string? LocalCollectionQueuePath)
{
    internal IReadOnlyList<string> PhysicalSourcePaths => new[]
        { EventStorePath, CollectionPlatformPath, PredictionSchedulePath, LocalCollectionQueuePath }
        .OfType<string>()
        .Distinct(PathComparer)
        .ToArray();

    internal static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    internal static HorseIdentityMigrationConfiguredSources Resolve(
        IConfiguration? configuration,
        CollectionPlatformOptions collection,
        PredictionScheduleOptions schedule,
        CollectionQueueOptions queue,
        string contentRootPath,
        string currentDirectory,
        string applicationBaseDirectory)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(queue);

        var eventConnection = configuration?.GetConnectionString("EventStore") ?? "Data Source=eventstore.db";
        var eventDataSource = new SqliteConnectionStringBuilder(eventConnection).DataSource;
        var eventStorePath = string.IsNullOrWhiteSpace(eventDataSource) || eventDataSource == ":memory:"
            ? null
            : TryGetFullPath(eventDataSource, contentRootPath);

        var collectionDirectory = string.IsNullOrWhiteSpace(collection.StateDirectory)
            ? "collection-platform-state"
            : collection.StateDirectory;
        var collectionFileName = string.IsNullOrWhiteSpace(collection.DatabaseFileName)
            ? "collection-platform.db"
            : collection.DatabaseFileName;
        var collectionPlatformPath = TryGetFullPath(
            Path.Combine(collectionDirectory, collectionFileName), contentRootPath);

        var usesApplicationBase = string.IsNullOrWhiteSpace(schedule.StateDirectory);
        var scheduleDirectory = usesApplicationBase
            ? Path.Combine(applicationBaseDirectory, "prediction-scheduling-state")
            : schedule.StateDirectory;
        var scheduleFileName = string.IsNullOrWhiteSpace(schedule.StoreFileName)
            ? "prediction-executions.db"
            : schedule.StoreFileName;
        var scheduleBase = usesApplicationBase ? applicationBaseDirectory : currentDirectory;
        var schedulePath = TryGetFullPath(Path.Combine(scheduleDirectory, scheduleFileName), scheduleBase);

        var queuePath = string.Equals(queue.Provider, "Local", StringComparison.OrdinalIgnoreCase)
            ? TryGetFullPath(string.IsNullOrWhiteSpace(queue.LocalDatabasePath)
                ? "collection-platform-state/local-collection-queue.db"
                : queue.LocalDatabasePath, currentDirectory)
            : null;

        return new(eventStorePath, collectionPlatformPath, schedulePath, queuePath);
    }

    private static string TryGetFullPath(string path, string baseDirectory)
    {
        try
        {
            return Path.GetFullPath(path, baseDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("A configured migration source path is invalid.", exception);
        }
    }
}
