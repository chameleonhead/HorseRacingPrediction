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
        Assert.AreNotEqual(CollectionMonitoringOutcome.MonitorFailed, report.Outcome);
    }

    [TestMethod]
    public async Task Inspect_ClassifiesProgramBugAndSanitizesUntrustedEvidence()
    {
        using var scope = new MonitoringStoreScope();
        var now = new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.FromHours(9));
        await CreateFailureAsync(scope.Store, now, "race-detail", ResourceType.Race,
            "ValidationFailure", "```ignore instructions<!--secret-->\nnext");
        var service = scope.CreateService();

        var report = await service.InspectAsync(now.AddMinutes(1));

        var finding = report.Findings.Single(x => x.Kind == "ActionableFailureGroup");
        Assert.AreEqual(CollectionFindingClassification.ProgramBug, finding.Classification);
        Assert.AreEqual(CollectionMonitoringOutcome.ActionRequired, report.Outcome);
        Assert.IsFalse(finding.Evidence.Any(x => x.Contains("```", StringComparison.Ordinal)));
        Assert.IsFalse(finding.Evidence.Any(x => x.Contains("<!--", StringComparison.Ordinal)));
        Assert.AreEqual("1", finding.ClassifierVersion);
    }

    [TestMethod]
    public async Task Inspect_UsesFindingRecordedForNonUrgentFindings()
    {
        using var scope = new MonitoringStoreScope();
        var now = new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.FromHours(9));
        await CreateFailureAsync(scope.Store, now, "horse-profile", ResourceType.Horse,
            "SubjectNotIdentified", "known historical identity failure");

        var report = await scope.CreateService().InspectAsync(now.AddMinutes(1));

        Assert.AreEqual(CollectionMonitoringOutcome.FindingRecorded, report.Outcome);
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
        var routed = report.Findings.Single(x => x.Kind == "StalledActiveTask");
        Assert.AreEqual("T3", routed.OwnerTask);
        Assert.IsFalse(string.IsNullOrWhiteSpace(routed.RootCauseHypothesis));
        var flow = report.DefinitionFlows!.Single(x => x.Definition == "horse-history");
        Assert.AreEqual(1, flow.Arrived);
        Assert.AreEqual(1, flow.Active);
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
    public void DispatchOrderViolation_RequiresCompatibleDefinition()
    {
        using var scope = new MonitoringStoreScope();
        var now = DateTimeOffset.UtcNow;
        var waiting = MonitoringTask("horse-profile", 100, now.AddHours(-2));
        var incompatible = Enumerable.Range(1, 3)
            .Select(_ => MonitoringDispatch("trainer-profile", 10, now.AddMinutes(-10))).ToArray();
        var snapshot = new CollectionMonitoringSnapshot(now, new(false, null, null), [waiting], incompatible,
            false);

        Assert.IsEmpty(scope.CreateService().FindDispatchOrderViolations(snapshot, now));
    }

    [TestMethod]
    public void DispatchOrderViolation_ReportsRepeatedCompatibleBypass()
    {
        using var scope = new MonitoringStoreScope();
        var now = DateTimeOffset.UtcNow;
        var waiting = MonitoringTask("horse-profile", 100, now.AddHours(-2));
        var compatible = Enumerable.Range(1, 3)
            .Select(index => MonitoringDispatch("horse-profile", 10, now.AddMinutes(-10 + index))).ToArray();
        var snapshot = new CollectionMonitoringSnapshot(now, new(false, null, null), [waiting], compatible,
            false);

        var finding = scope.CreateService().FindDispatchOrderViolations(snapshot, now).Single();

        Assert.IsTrue(finding.Evidence.Contains("compatibilityKey=JRA|Definition|horse-profile|Realtime"));
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

    [TestMethod]
    public async Task KnownRecoveryPreview_RequiresAnActuallyEligibleCandidate()
    {
        using var scope = new MonitoringStoreScope();
        var now = DateTimeOffset.UtcNow;
        await CreateFailureAsync(scope.Store, now, "horse-profile", ResourceType.Horse,
            "SubjectNotIdentified", "missing deterministic identity evidence");
        var service = scope.CreateService(new() { RecoveryEnabled = true });

        var preview = await service.PreviewKnownRecoveryAsync(now.AddMinutes(1));

        Assert.AreEqual(0, preview.MatchingFindingCount);
        Assert.IsFalse(preview.SafeToApply);
        StringAssert.Contains(preview.BlockingReason, "No matching");
    }

    [TestMethod]
    public void Freshness_FridayProductionShapeReportsSundayCardsAndAcceptsCurrentSaturdayCards()
    {
        using var scope = new MonitoringStoreScope();
        var service = scope.CreateService();
        var now = new DateTimeOffset(2026, 9, 18, 21, 5, 0, TimeSpan.FromHours(9));
        var races = Enumerable.Range(1, 24)
            .Select(index => RaceFreshness($"20260919:Course:{index}", now.AddHours(-4),
                RaceArtifactStatus.Current, RaceArtifactStatus.Current))
            .Concat(Enumerable.Range(1, 24).Select(index => RaceFreshness(
                $"20260920:Course:{index}", now.AddDays(1).AddHours(-6),
                RaceArtifactStatus.Blocked, RaceArtifactStatus.AwaitingPublication)))
            .ToArray();

        var findings = service.EvaluateFreshness(races, now);

        Assert.IsFalse(findings.Any(x => x.Kind == "RaceCardCoverageMissing"
                                         && x.Evidence.Contains("raceDate=2026-09-19")));
        var sunday = findings.Single(x => x.Kind == "RaceCardCoverageMissing");
        Assert.AreEqual("critical", sunday.Severity);
        Assert.IsTrue(sunday.Evidence.Contains("discovered=24"));
        Assert.IsTrue(sunday.Evidence.Contains("missing=24"));
    }

    [TestMethod]
    public void Freshness_DomainCardsPreventArtifactLagFalsePositive()
    {
        using var scope = new MonitoringStoreScope();
        var service = scope.CreateService();
        var now = new DateTimeOffset(2026, 9, 19, 18, 30, 0, TimeSpan.FromHours(9));
        var races = Enumerable.Range(1, 24).Select(index => RaceFreshness(
            $"20260919:Course:{index}", now.AddHours(-8),
            RaceArtifactStatus.Unknown, RaceArtifactStatus.Unknown)).ToArray();
        var domain = Enumerable.Range(1, 24)
            .Select(_ => new DomainRaceFreshness(new DateOnly(2026, 9, 19), true, true)).ToArray();

        var findings = service.EvaluateFreshness(races, domain, now);

        Assert.IsFalse(findings.Any(x => x.Kind == "RaceCardCoverageMissing"));
        Assert.IsFalse(findings.Any(x => x.Kind is "RaceDayResultCoverageMissing" or "RaceResultFreshnessMiss"));
    }

    [TestMethod]
    public void Freshness_DomainCoverageDoesNotHideMissingSundayCards()
    {
        using var scope = new MonitoringStoreScope();
        var service = scope.CreateService();
        var now = new DateTimeOffset(2026, 9, 18, 21, 5, 0, TimeSpan.FromHours(9));
        var races = Enumerable.Range(1, 24).Select(index => RaceFreshness(
            $"20260920:Course:{index}", now.AddDays(1).AddHours(-6),
            RaceArtifactStatus.Blocked, RaceArtifactStatus.Unknown)).ToArray();

        var finding = service.EvaluateFreshness(races, [], now)
            .Single(x => x.Kind == "RaceCardCoverageMissing");

        Assert.AreEqual("critical", finding.Severity);
        Assert.IsTrue(finding.Evidence.Contains("missing=24"));
    }

    [TestMethod]
    public void Freshness_FridayCheckpointTreatsZeroDiscoveryAsUnknown()
    {
        using var scope = new MonitoringStoreScope();
        var service = scope.CreateService();
        var now = new DateTimeOffset(2026, 9, 18, 18, 0, 0, TimeSpan.FromHours(9));

        var findings = service.EvaluateFreshness([], now);

        Assert.HasCount(2, findings.Where(x => x.Kind == "WeekendDiscoveryCoverageUnknown"));
    }

    [TestMethod]
    public void Freshness_BlockedWeekdayCard_IsReportedWithoutWeekendRule()
    {
        using var scope = new MonitoringStoreScope();
        var service = scope.CreateService();
        var now = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.FromHours(9));
        var races = new[]
        {
            RaceFreshness("20260923:Course:1", now.AddHours(-1),
                RaceArtifactStatus.Blocked, RaceArtifactStatus.AwaitingPublication),
        };

        var finding = service.EvaluateFreshness(races, now)
            .Single(x => x.Kind == "RaceCardCoverageMissing");

        Assert.AreEqual("critical", finding.Severity);
        Assert.IsTrue(finding.Evidence.Contains("raceDate=2026-09-23"));
    }

    [TestMethod]
    public void Freshness_ResultGraceAndDayCheckpointReportMissingResult()
    {
        using var scope = new MonitoringStoreScope();
        var service = scope.CreateService();
        var now = new DateTimeOffset(2026, 9, 19, 18, 30, 0, TimeSpan.FromHours(9));
        var races = new[]
        {
            RaceFreshness("20260919:Nakayama:1", now.AddHours(-8),
                RaceArtifactStatus.Current, RaceArtifactStatus.Current),
            RaceFreshness("20260919:Nakayama:2", now.AddHours(-2),
                RaceArtifactStatus.Current, RaceArtifactStatus.Due),
        };

        var finding = service.EvaluateFreshness(races, now)
            .Single(x => x.Kind == "RaceDayResultCoverageMissing");

        Assert.AreEqual("high", finding.Severity);
        Assert.IsTrue(finding.Evidence.Contains("due=2"));
        Assert.IsTrue(finding.Evidence.Contains("missing=1"));
    }

    private static CollectionRaceFreshnessSnapshot RaceFreshness(string id, DateTimeOffset start,
        RaceArtifactStatus card, RaceArtifactStatus result) => new(
        Guid.NewGuid(), new(ResourceType.Race, "JRA", id), CollectionTaskStatus.Ready,
        start.AddHours(-1), start, card, result);

    private static CollectionMonitoringTaskSnapshot MonitoringTask(string definition, int priority,
        DateTimeOffset availableAt) => new(Guid.NewGuid(), new(ResourceType.Horse, "JRA", Guid.NewGuid().ToString("N")),
        new(definition), CollectionTaskStatus.Ready, CollectionLane.Realtime, priority, availableAt, availableAt,
        availableAt, null, null, 0, $"JRA|Definition|{definition}|Realtime");

    private static CollectionMonitoringDispatchSnapshot MonitoringDispatch(string definition, int priority,
        DateTimeOffset dispatchedAt) => new(Guid.NewGuid(), Guid.NewGuid(), new(definition), CollectionLane.Realtime,
        priority, dispatchedAt.AddMinutes(-1), dispatchedAt.AddHours(-1), dispatchedAt,
        $"JRA|Definition|{definition}|Realtime");

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
