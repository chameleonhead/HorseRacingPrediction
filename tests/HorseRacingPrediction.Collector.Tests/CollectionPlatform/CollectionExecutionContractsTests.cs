using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Collector.Http;
using HorseRacingPrediction.Scraping.Jra.Pages;
using Microsoft.Extensions.Options;
using System.Net;

using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Subjects;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class CollectionExecutionContractsTests
{
    [TestMethod]
    public void Registry_RejectsDuplicateDefinitions()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => new CollectionDefinitionHandlerRegistry(
            [new Handler("horse-profile", CollectionResourceType.Horse), new Handler("horse-profile", CollectionResourceType.Horse)]));
    }

    [TestMethod]
    public void Registry_RejectsResourceTypeMismatch()
    {
        var registry = new CollectionDefinitionHandlerRegistry([new Handler("horse-profile", CollectionResourceType.Horse)]);

        Assert.ThrowsExactly<InvalidOperationException>(() => registry.Resolve(new("horse-profile"), CollectionResourceType.Trainer));
    }

    [TestMethod]
    public void Allocator_PrioritizesRealtime()
    {
        var now = DateTimeOffset.UtcNow;
        var realtime = new FairCollectionCandidate(Guid.NewGuid(), CollectionLane.Realtime, 90, now, now);
        var background = new FairCollectionCandidate(Guid.NewGuid(), CollectionLane.Background, 100, now, now);

        Assert.AreEqual(realtime.TaskId, new CollectionLaneAllocator().Select([background, realtime], now)!.TaskId);
    }

    [TestMethod]
    public void Allocator_PrioritizesHigherPriorityBeforeScanCursor()
    {
        var now = DateTimeOffset.UtcNow;
        var beforeCursor = new FairCollectionCandidate(Guid.NewGuid(), CollectionLane.Realtime, 1000,
            now.AddMinutes(-2), now.AddMinutes(-2), Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var afterCursor = new FairCollectionCandidate(Guid.NewGuid(), CollectionLane.Realtime, 70,
            now, now, Guid.Parse("00000000-0000-0000-0000-000000000003"));
        var state = CollectionLaneDispatchState.Empty with
        {
            ScanAvailableAt = now.AddMinutes(-1),
            ScanOutboxId = Guid.Parse("00000000-0000-0000-0000-000000000002"),
        };

        var selected = new CollectionLaneAllocator().Select([beforeCursor, afterCursor], now, state);

        Assert.AreEqual(beforeCursor.TaskId, selected!.TaskId);
    }

    [TestMethod]
    public void Allocator_DoesNotStarveBackground()
    {
        var now = DateTimeOffset.UtcNow;
        var allocator = new CollectionLaneAllocator(maxConsecutiveRealtime: 2);
        var background = new FairCollectionCandidate(Guid.NewGuid(), CollectionLane.Background, 10, now, now);
        FairCollectionCandidate Realtime() => new(Guid.NewGuid(), CollectionLane.Realtime, 100, now, now);
        var state = CollectionLaneDispatchState.Empty;

        for (var index = 0; index < 2; index++)
        {
            var selected = allocator.Select([background, Realtime()], now, state)!;
            Assert.AreEqual(CollectionLane.Realtime, selected.Lane);
            state = state.Advance(selected.Lane);
        }
        Assert.AreEqual(CollectionLane.Background, allocator.Select([background, Realtime()], now, state)!.Lane);
    }

    [TestMethod]
    public void Allocator_AllLanesDue_UsesEightyTenTenRotation()
    {
        var now = DateTimeOffset.UtcNow;
        var allocator = new CollectionLaneAllocator();
        var candidates = new[]
        {
            new FairCollectionCandidate(Guid.NewGuid(), CollectionLane.Realtime, 100, now, now),
            new FairCollectionCandidate(Guid.NewGuid(), CollectionLane.Normal, 50, now, now),
            new FairCollectionCandidate(Guid.NewGuid(), CollectionLane.Background, 10, now, now),
        };
        var state = CollectionLaneDispatchState.Empty;
        var actual = new List<CollectionLane>();

        for (var index = 0; index < 10; index++)
        {
            var selected = allocator.Select(candidates, now, state)!;
            actual.Add(selected.Lane);
            state = state.Advance(selected.Lane);
        }

        CollectionAssert.AreEqual(new[]
        {
            CollectionLane.Realtime, CollectionLane.Realtime, CollectionLane.Realtime, CollectionLane.Realtime,
            CollectionLane.Normal,
            CollectionLane.Realtime, CollectionLane.Realtime, CollectionLane.Realtime, CollectionLane.Realtime,
            CollectionLane.Background,
        }, actual);
    }

    [TestMethod]
    public void Allocator_WithoutRealtime_AlternatesNormalAndBackground()
    {
        var now = DateTimeOffset.UtcNow;
        var allocator = new CollectionLaneAllocator();
        var candidates = new[]
        {
            new FairCollectionCandidate(Guid.NewGuid(), CollectionLane.Normal, 10, now, now),
            new FairCollectionCandidate(Guid.NewGuid(), CollectionLane.Background, 100, now, now),
        };
        var state = CollectionLaneDispatchState.Empty;
        var actual = new List<CollectionLane>();

        for (var index = 0; index < 4; index++)
        {
            var selected = allocator.Select(candidates, now, state)!;
            actual.Add(selected.Lane);
            state = state.Advance(selected.Lane);
        }

        CollectionAssert.AreEqual(new[]
        {
            CollectionLane.Normal, CollectionLane.Background, CollectionLane.Normal, CollectionLane.Background,
        }, actual);
    }

    [TestMethod]
    public void FailureClassifier_PreservesAvailableHttpStatus()
    {
        var completion = CollectionAttemptFailureClassifier.FromException(
            new HttpRequestException("unavailable", null, HttpStatusCode.ServiceUnavailable));

        Assert.AreEqual(CollectionAttemptResult.TransientFailure, completion.Result);
        Assert.AreEqual(503, completion.HttpStatusCode);
    }

    [TestMethod]
    public async Task StaleProfileLease_IsolatedThroughCompletionWhileUnknownConflictStillPauses()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"profile-lease-classifier-{Guid.NewGuid():N}");
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            { StateDirectory = directory }));
            var definition = new CollectionDefinitionId("horse-profile");
            await store.RegisterDefinitionAsync(definition, "Horse", CollectionResourceType.Horse, 1, "test", false);
            var stale = CollectionAttemptFailureClassifier.FromException(new StaleCollectionTaskLeaseException());
            Assert.AreEqual(CollectionAttemptResult.TransientFailure, stale.Result);
            Assert.AreEqual("StaleSubjectProfileLease", stale.ErrorCode);
            Assert.AreEqual(CollectionFailureImpact.Isolated, stale.FailureImpact);

            var now = DateTimeOffset.UtcNow;
            var staleTask = await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", "PROFILE-STALE"),
                definition, 1, CollectionReason.Initial, now);
            var executor = new CollectionTaskExecutor(store, new CollectionDefinitionHandlerRegistry(
                [new ThrowingHandler(new StaleCollectionTaskLeaseException())]));
            Assert.IsTrue(await executor.ExecuteAsync(new(staleTask.TaskId!.Value, 1), now, TimeSpan.FromMinutes(5)));
            Assert.IsFalse((await store.GetPipelineStateAsync()).IsPaused);
            Assert.AreEqual(CollectionAttemptResult.TransientFailure,
                (await store.GetAttemptsAsync(staleTask.TaskId.Value)).Single().Result);

            var integrityTask = await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", "PROFILE-INTEGRITY"),
                definition, 1, CollectionReason.Initial, now.AddSeconds(1));
            executor = new CollectionTaskExecutor(store, new CollectionDefinitionHandlerRegistry(
                [new ThrowingHandler(new HttpRequestException("identity conflict", null, HttpStatusCode.Conflict))]));
            Assert.IsTrue(await executor.ExecuteAsync(new(integrityTask.TaskId!.Value, 1), now.AddSeconds(1),
                TimeSpan.FromMinutes(5)));
            Assert.IsTrue((await store.GetPipelineStateAsync()).IsPaused);
            var integrityAttempt = (await store.GetAttemptsAsync(integrityTask.TaskId.Value)).Single();
            Assert.AreEqual(CollectionAttemptResult.PermanentFailure, integrityAttempt.Result);
            Assert.AreEqual(409, integrityAttempt.HttpStatusCode);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task ProfileClientUnknownLengthConflictFlowsThroughExecutorAndCompletion()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"profile-lease-http-executor-{Guid.NewGuid():N}");
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            { StateDirectory = directory }));
            var definition = new CollectionDefinitionId("horse-profile");
            await store.RegisterDefinitionAsync(definition, "Horse", CollectionResourceType.Horse, 1, "test", false);
            using var http = new HttpClient(new CollectionWorkerLeaseHandler
            {
                InnerHandler = new ProfileConflictHandler(),
            })
            { BaseAddress = new Uri("https://api.test/") };
            var client = new JraSubjectProfileApiClient(http);
            var executor = new CollectionTaskExecutor(store, new CollectionDefinitionHandlerRegistry(
                [new ProfileSaveHandler(client)]));
            var now = DateTimeOffset.UtcNow;

            var staleTask = await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", "PROFILE-STALE-HTTP"),
                definition, 1, CollectionReason.Initial, now);
            Assert.IsTrue(await executor.ExecuteAsync(new(staleTask.TaskId!.Value, 1), now, TimeSpan.FromMinutes(5)));
            var staleAttempt = (await store.GetAttemptsAsync(staleTask.TaskId.Value)).Single();
            Assert.AreEqual(CollectionAttemptResult.TransientFailure, staleAttempt.Result);
            Assert.AreEqual("StaleSubjectProfileLease", staleAttempt.ErrorCode);
            Assert.IsFalse((await store.GetPipelineStateAsync()).IsPaused);

            var identityTask = await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", "PROFILE-IDENTITY-HTTP"),
                definition, 1, CollectionReason.Initial, now.AddSeconds(1));
            Assert.IsTrue(await executor.ExecuteAsync(new(identityTask.TaskId!.Value, 1), now.AddSeconds(1),
                TimeSpan.FromMinutes(5)));
            var identityAttempt = (await store.GetAttemptsAsync(identityTask.TaskId.Value)).Single();
            Assert.AreEqual(CollectionAttemptResult.PermanentFailure, identityAttempt.Result);
            Assert.AreEqual(409, identityAttempt.HttpStatusCode);
            Assert.IsTrue((await store.GetPipelineStateAsync()).IsPaused);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void CalendarReadinessTimeout_RemainsTransientAcrossCollectionClassifiers()
    {
        var exception = new TimeoutException("Calendar readiness timed out.");
        var location = new ResourceLocationCandidate(
            42,
            new Uri("https://www.jra.go.jp/keiba/calendar/"),
            ResourceLocationSource.Generated,
            ResourceLocationStatus.Active,
            null);

        var attempt = CollectionAttemptFailureClassifier.FromException(exception);
        var resource = ResourceLocationOutcomeClassifier.Failed(location, exception);

        Assert.AreEqual(CollectionAttemptResult.TransientFailure, attempt.Result);
        Assert.AreEqual(CollectionAttemptResult.TransientFailure, resource.Result);
    }

    [TestMethod]
    public void ClosedBrowserSession_IsCanonicalTransientFailure()
    {
        var completion = CollectionAttemptFailureClassifier.FromException(
            new InvalidOperationException("wrapper", new TargetClosedException()));

        Assert.AreEqual(CollectionAttemptResult.TransientFailure, completion.Result);
        Assert.AreEqual("TargetClosedException", completion.ErrorCode);
        Assert.AreEqual(CollectionFailureImpact.StopPipeline, completion.FailureImpact);
    }

    [TestMethod]
    public void CompletedCalendarParseFailure_RemainsStructural()
    {
        var exception = new JraPageParseException(
            JraPageKind.Calendar,
            "https://www.jra.go.jp/keiba/calendar/",
            "Malformed completed calendar.");
        var location = new ResourceLocationCandidate(
            42,
            new Uri(exception.Url),
            ResourceLocationSource.Generated,
            ResourceLocationStatus.Active,
            null);

        var attempt = CollectionAttemptFailureClassifier.FromException(exception);
        var resource = ResourceLocationOutcomeClassifier.Failed(location, exception);

        Assert.AreEqual(CollectionAttemptResult.PermanentFailure, attempt.Result);
        Assert.AreEqual(CollectionAttemptResult.UnexpectedPage, resource.Result);
    }

    [TestMethod]
    public void MissingHorseSearchField_RemainsStructural()
    {
        var exception = new InvalidOperationException(
            "フィールド 'iv_h_name' が見つかりませんでした。Url=https://www.jra.go.jp/JRADB/accessO.html");

        var attempt = CollectionAttemptFailureClassifier.FromException(exception);

        Assert.AreEqual(CollectionAttemptResult.PermanentFailure, attempt.Result);
        Assert.AreEqual(nameof(InvalidOperationException), attempt.ErrorCode);
        StringAssert.Contains(attempt.ErrorMessage!, "iv_h_name");
    }

    [TestMethod]
    public void TaskContext_FillsMissingIdentificationAndPreservesSpecificIdentification()
    {
        var task = new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(CollectionResourceType.Horse, "JRA", "horse-1"), new("horse-profile"), 1,
            CollectionReason.Initial, CollectionLane.Normal, 50, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), null, new Dictionary<string, string>());

        var fallback = CollectionAttemptFailureClassifier.WithTaskContext(
            new(CollectionAttemptResult.PermanentFailure, "Failure"), task);
        var specific = CollectionAttemptFailureClassifier.WithTaskContext(
            new(CollectionAttemptResult.UnexpectedPage, "Failure", PageIdentification: "LoginPage"), task);

        Assert.AreEqual("Definition=horse-profile; Resource=Horse:JRA:horse-1", fallback.PageIdentification);
        Assert.AreEqual("LoginPage", specific.PageIdentification);
    }

    [TestMethod]
    public async Task LocalExecutor_CancellationIsPersistedAsRetryableWithIndependentCompletionToken()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"local-executor-cancel-{Guid.NewGuid():N}");
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            { StateDirectory = directory }));
            var definition = new CollectionDefinitionId("horse-profile");
            var resource = new ResourceKey(CollectionResourceType.Horse, "JRA", "H-CANCEL");
            await store.RegisterDefinitionAsync(definition, "Horse", CollectionResourceType.Horse, 1, "initial", false);
            var request = await store.RequestAsync(resource, definition, 1, CollectionReason.Initial,
                DateTimeOffset.UtcNow);
            using var cancellation = new CancellationTokenSource();
            var executor = new CollectionTaskExecutor(store,
                new CollectionDefinitionHandlerRegistry([new CancellingHandler(cancellation)]));

            Assert.IsTrue(await executor.ExecuteAsync(new(request.TaskId!.Value, 1), DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(5), cancellation.Token));

            var task = (await store.GetTasksAsync()).Single(x => x.TaskId == request.TaskId!.Value);
            Assert.AreEqual(CollectionTaskStatus.Ready, task.Status);
            Assert.IsTrue(task.AvailableAt > DateTimeOffset.UtcNow);
            var attempts = await store.GetAttemptsAsync(request.TaskId!.Value);
            Assert.AreEqual(CollectionAttemptResult.TransientFailure, attempts.Single().Result);
            Assert.AreEqual("Definition=horse-profile; Resource=Horse:JRA:H-CANCEL",
                attempts.Single().PageIdentification);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class Handler(string id, CollectionResourceType type) : ICollectionDefinitionHandler
    {
        public CollectionDefinitionId DefinitionId => new(id);
        public CollectionResourceType ResourceType => type;
        public Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task, CancellationToken cancellationToken)
            => Task.FromResult(new CollectionAttemptCompletion(CollectionAttemptResult.Succeeded));
    }

    private sealed class ThrowingHandler(Exception exception) : ICollectionDefinitionHandler
    {
        public CollectionDefinitionId DefinitionId => new("horse-profile");
        public CollectionResourceType ResourceType => CollectionResourceType.Horse;
        public Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task, CancellationToken cancellationToken)
            => throw exception;
    }

    private sealed class ProfileSaveHandler(JraSubjectProfileApiClient client) : ICollectionDefinitionHandler
    {
        public CollectionDefinitionId DefinitionId => new("horse-profile");
        public CollectionResourceType ResourceType => CollectionResourceType.Horse;
        public async Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task,
            CancellationToken cancellationToken)
        {
            using var lease = CollectionWorkerLeaseContext.Push(task.TaskId, task.LeaseToken);
            var url = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=lease-test";
            var profile = new JraSubjectProfileDto("Horse", "テスト馬", url, url,
                new Dictionary<string, string>(), DateTimeOffset.UtcNow);
            await client.SaveAsync("Horse", task.Resource.Id, profile, cancellationToken);
            return new(CollectionAttemptResult.Succeeded);
        }
    }

    private sealed class ProfileConflictHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.IsTrue(request.Headers.Contains("X-Collection-Task-Id"));
            Assert.IsTrue(request.Headers.Contains("X-Collection-Lease-Token"));
            var body = request.RequestUri!.AbsolutePath.Contains("PROFILE-IDENTITY-HTTP", StringComparison.Ordinal)
                ? "[\"同定不能: 保存済みの対象とプロフィールが一致しません。\"]"
                : "{\"code\":\"StaleSubjectProfileLease\"}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
            { Content = new UnknownLengthContent(body) });
        }
    }

    private sealed class UnknownLengthContent(string body) : HttpContent
    {
        private readonly byte[] _bytes = System.Text.Encoding.UTF8.GetBytes(body);
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            stream.WriteAsync(_bytes).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(_bytes));
    }

    private sealed class CancellingHandler(CancellationTokenSource cancellation) : ICollectionDefinitionHandler
    {
        public CollectionDefinitionId DefinitionId => new("horse-profile");
        public CollectionResourceType ResourceType => CollectionResourceType.Horse;
        public Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task,
            CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }
    }

    private sealed class TargetClosedException()
        : Exception("Target page, context or browser has been closed");
}
