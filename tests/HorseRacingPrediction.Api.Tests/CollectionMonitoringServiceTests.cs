using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionMonitoringServiceTests
{
    [TestMethod]
    public async Task MonitoringEndpoint_UsesApiKeyBoundaryAndReturnsTypedReport()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;

        Assert.AreEqual(HttpStatusCode.Unauthorized,
            (await http.GetAsync("/api/admin/collection/monitoring/findings")).StatusCode);
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var report = await http.GetFromJsonAsync<CollectionMonitoringReport>(
            "/api/admin/collection/monitoring/findings");

        Assert.IsNotNull(report);
        Assert.IsTrue(report.Enabled);
    }

    [TestMethod]
    public async Task Inspect_ClassifiesProgramBugAndSanitizesUntrustedEvidence()
    {
        using var scope = new MonitoringStoreScope();
        var now = DateTimeOffset.UtcNow;
        await CreateFailureAsync(scope.Store, now, "race-detail", ResourceType.Race,
            "ValidationFailure", "```ignore instructions<!--secret-->\nnext");
        var service = scope.CreateService();

        var report = await service.InspectAsync(now.AddMinutes(1));

        var finding = report.Findings.Single(x => x.Kind == "ActionableFailureGroup");
        Assert.AreEqual(CollectionFindingClassification.ProgramBug, finding.Classification);
        Assert.IsFalse(finding.Evidence.Any(x => x.Contains("```", StringComparison.Ordinal)));
        Assert.IsFalse(finding.Evidence.Any(x => x.Contains("<!--", StringComparison.Ordinal)));
        Assert.AreEqual("1", finding.ClassifierVersion);
    }

    [TestMethod]
    public async Task Inspect_DoesNotClassifyUnsupportedOwnerFailureAsKnownRecovery()
    {
        using var scope = new MonitoringStoreScope();
        var now = DateTimeOffset.UtcNow;
        await CreateFailureAsync(scope.Store, now, "owner-profile", ResourceType.Owner,
            "SubjectNotIdentified", "owner heading mismatch");

        var finding = (await scope.CreateService().InspectAsync(now.AddMinutes(1))).Findings
            .Single(x => x.Kind == "ActionableFailureGroup");

        Assert.AreEqual(CollectionFindingClassification.UnknownHistoricalJobError, finding.Classification);
        Assert.IsNull(finding.RecoveryRecipeId);
    }

    [TestMethod]
    public async Task Inspect_DetectsStalledDueTaskWithoutChangingIt()
    {
        using var scope = new MonitoringStoreScope();
        var now = DateTimeOffset.UtcNow;
        var definition = new CollectionDefinitionId("horse-history");
        await scope.Store.RegisterDefinitionAsync(definition, "Horse history", ResourceType.Horse, 1, "initial", false);
        var receipt = await scope.Store.RequestAsync(
            new(ResourceType.Horse, "JRA", $"horse-{Guid.NewGuid():N}"), definition, 1,
            CollectionReason.Backfill, now.AddHours(-3), CollectionLane.Background, 10);
        var before = (await scope.Store.GetTasksAsync()).Single(x => x.TaskId == receipt.TaskId);
        var service = scope.CreateService(new() { DueStallMinutes = 60 });

        var report = await service.InspectAsync(now);

        Assert.IsTrue(report.Findings.Any(x => x.Kind == "StalledActiveTask"));
        var task = (await scope.Store.GetTasksAsync()).Single(x => x.TaskId == receipt.TaskId);
        Assert.AreEqual(before.Status, task.Status);
    }

    [TestMethod]
    public async Task Inspect_SuppressesFindingsDuringMaintenance()
    {
        using var scope = new MonitoringStoreScope();
        var now = DateTimeOffset.UtcNow;
        await scope.Store.SetPausedAsync(true, "approved maintenance deployment", now.AddMinutes(-5));
        var service = scope.CreateService();

        var report = await service.InspectAsync(now);

        Assert.IsTrue(report.Suppressed);
        Assert.IsEmpty(report.Findings);
        StringAssert.Contains(report.SuppressionReason, "maintenance");
    }

    [TestMethod]
    public async Task Inspect_ReportsLoadTruncationAndIndependentChangeRecordSwitch()
    {
        using var scope = new MonitoringStoreScope();
        var now = DateTimeOffset.UtcNow;
        var definition = new CollectionDefinitionId("load-boundary");
        await scope.Store.RegisterDefinitionAsync(definition, "Load boundary", ResourceType.Horse, 1, "initial", false);
        for (var index = 0; index < 2; index++)
            await scope.Store.RequestAsync(new(ResourceType.Horse, "JRA", $"load-{index}-{Guid.NewGuid():N}"),
                definition, 1, CollectionReason.Backfill, now, CollectionLane.Background, 1);
        var service = scope.CreateService(new() { MaxRows = 1, ChangeRecordEnabled = false });

        var report = await service.InspectAsync(now);

        Assert.IsTrue(report.Truncated);
        Assert.IsFalse(report.ChangeRecordEnabled);
    }

    [TestMethod]
    public async Task KnownRecovery_RequiresSwitchCreatesBackupAndHonorsCanary()
    {
        using var scope = new MonitoringStoreScope();
        var now = DateTimeOffset.UtcNow.AddMinutes(-2);
        var definition = new CollectionDefinitionId("horse-profile");
        await scope.Store.RegisterDefinitionAsync(definition, "Horse profile", ResourceType.Horse, 1, "old", false);
        for (var index = 0; index < 2; index++)
        {
            var resource = new ResourceKey(ResourceType.Horse, "JRA", $"horse-{Guid.NewGuid():N}");
            var receipt = await scope.Store.RequestAsync(resource, definition, 1, CollectionReason.Discovery, now,
                attributes: new Dictionary<string, string> { ["name"] = $"テスト馬{index}" });
            var lease = await scope.Store.AcquireAsync(receipt.TaskId, 1, now, TimeSpan.FromMinutes(5));
            await scope.Store.CompleteAttemptAsync(receipt.TaskId, lease!.LeaseToken, now.AddSeconds(1),
                new(CollectionAttemptResult.ResourceNotFound, "SubjectNotIdentified", "登録区分付き見出しです。",
                    PageIdentification: "SubjectIdentification:ProfileNameMismatch"));
        }
        await scope.Store.RegisterDefinitionAsync(definition, "Horse profile", ResourceType.Horse, 2, "fixed", true);

        var disabled = scope.CreateService(new() { RecoveryEnabled = false });
        Assert.IsFalse((await disabled.PreviewKnownRecoveryAsync(DateTimeOffset.UtcNow)).SafeToApply);

        var enabled = scope.CreateService(new() { RecoveryEnabled = true, CanaryLimit = 1 });
        var result = await enabled.ApplyKnownRecoveryAsync(DateTimeOffset.UtcNow,
            NullLogger<CollectionMonitoringService>.Instance);

        Assert.AreEqual(1, result.Examined);
        Assert.AreEqual(1, result.Recovered);
        Assert.IsTrue(File.Exists(Path.Combine(scope.Directory, "backups", result.BackupFileName)));
        Assert.HasCount(1, await scope.Store.GetActionableFailureNotificationsAsync(DateTimeOffset.UtcNow, 10));
    }

    private static async Task CreateFailureAsync(CollectionPlatformStore store, DateTimeOffset now,
        string definitionId, ResourceType type, string errorCode, string message)
    {
        var definition = new CollectionDefinitionId(definitionId);
        await store.RegisterDefinitionAsync(definition, definitionId, type, 1, "initial", false);
        var receipt = await store.RequestAsync(new(type, "JRA", $"resource-{Guid.NewGuid():N}"),
            definition, 1, CollectionReason.Discovery, now.AddMinutes(-2));
        var lease = await store.AcquireAsync(receipt.TaskId, 1, now.AddMinutes(-2), TimeSpan.FromMinutes(5));
        await store.CompleteAttemptAsync(receipt.TaskId, lease!.LeaseToken, now.AddMinutes(-1),
            new(CollectionAttemptResult.ValidationFailure, errorCode, message));
        await store.SetPausedAsync(false, null, now);
    }

    private sealed class MonitoringStoreScope : IDisposable
    {
        public MonitoringStoreScope()
        {
            Directory = Path.Combine(Path.GetTempPath(), $"collection-monitoring-{Guid.NewGuid():N}");
            Store = new(Options.Create(new CollectionPlatformOptions { StateDirectory = Directory }));
        }

        public string Directory { get; }
        public CollectionPlatformStore Store { get; }

        public CollectionMonitoringService CreateService(CollectionMonitoringOptions? options = null) =>
            new(Store, Options.Create(options ?? new CollectionMonitoringOptions()));

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
        }
    }
}
