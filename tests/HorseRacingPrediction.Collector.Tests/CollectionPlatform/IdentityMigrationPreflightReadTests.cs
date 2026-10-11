using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class IdentityMigrationPreflightReadTests
{
    [TestMethod]
    public void ResolveDatabasePath_UsesConfiguredCollectionFileWithoutCreatingStoreOrDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "identity-preflight-path", Guid.NewGuid().ToString("N"));
        var stateDirectory = Path.Combine(root, "configured-state");
        try
        {
            var path = CollectionPlatformStore.ResolveIdentityMigrationPreflightDatabasePath(
                Options.Create(new CollectionPlatformOptions
                {
                    StateDirectory = stateDirectory,
                    DatabaseFileName = "configured.db",
                }));

            Assert.AreEqual(Path.Combine(stateDirectory, "configured.db"), path);
            Assert.IsFalse(Directory.Exists(stateDirectory));
            Assert.IsFalse(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
