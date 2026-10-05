using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using EventFlow.EntityFramework;
using HorseRacingPrediction.Infrastructure.Persistence;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using HorseRacingPrediction.Scraping.Jra.Workflow;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionPlatformDiscoveryEndToEndTests
{
    [TestMethod]
    public async Task DiscoveryRequest_TraversesApiOutboxSqsContractWorkerAndCreatesChildRequests()
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-discovery-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            {
                StateDirectory = directory,
            }));
            await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", CollectionResourceType.Race,
                1, "initial", false);
            await store.RegisterDefinitionAsync(new("race-detail"), "Race detail", CollectionResourceType.Race,
                HorseRacingPrediction.Contracts.Collection.CollectionDefinitionRevisions.RaceDetail, "artifact state machine", false);
            await store.RegisterDefinitionAsync(new("race-odds"), "Race odds", CollectionResourceType.RaceOdds,
                1, "initial", false);

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(store);
            builder.Services.AddSingleton<ICollectionDispatchTelemetry, NullCollectionDispatchTelemetry>();
            using var domain = new SqliteDbContextProvider();
            builder.Services.AddSingleton<IDbContextProvider<EventStoreDbContext>>(domain);
            var app = builder.Build();
            app.MapCollectionApiV2Endpoints();
            await app.StartAsync();
            await using var appLifetime = app;
            using var client = app.GetTestClient();

            var date = new DateOnly(2026, 9, 12);
            using var createResponse = await client.PostAsJsonAsync("api/v2/admin/collection/tasks", new
            {
                Task = new
                {
                    Mode = "Resource",
                    Resource = new
                    {
                        ResourceType = CollectionResourceType.Race,
                        Provider = "JRA",
                        ResourceId = $"discovery:{date:yyyyMMdd}",
                        DefinitionId = "race-discovery",
                        RequestedRevision = 1,
                        Reason = CollectionReason.Discovery,
                        Lane = CollectionLane.Realtime,
                        Priority = 100,
                        EffectiveDate = date,
                    }
                }
            });
            createResponse.EnsureSuccessStatusCode();

            var queue = new RecordingQueue();
            var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue,
                Options.Create(new CollectionQueueOptions
                {
                    Enabled = true,
                    DispatchBatchSize = 1,
                    AggregationDelayMilliseconds = 0
                }),
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance);
            await dispatcher.DispatchOnceAsync(CancellationToken.None);
            Assert.HasCount(1, queue.MessageBodies);

            // This serialize/deserialize boundary is the exact SQS body contract consumed by Lambda --once.
            var envelope = JsonSerializer.Deserialize<CollectionDispatchEnvelope>(queue.MessageBodies[0],
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.IsNotNull(envelope);
            var notification = envelope.Tasks.Single();

            var race = new RaceId(date, RaceCourse.Tokyo, 11);
            var sessions = new DiscoverySessionFactory(date, race);
            var schedule = new ScheduleWorkflow(date);
            var requestSink = new CollectionRequestApiClient(client);
            var handler = new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, requestSink,
                timeProvider: new FixedTimeProvider(
                    new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero)));
            var worker = new CollectionPlatformWorkerClient(client,
                new CollectionDefinitionHandlerRegistry([handler]));

            await worker.ExecuteAsync(new(notification.TaskId, notification.DispatchGeneration), CancellationToken.None);

            var tasks = await store.GetTasksAsync(limit: 10);
            var discovery = tasks.Single(x => x.Definition.Value == "race-discovery");
            Assert.AreEqual(CollectionTaskStatus.Succeeded, discovery.Status);
            var detail = tasks.Single(x => x.Definition.Value == "race-detail");
            var odds = tasks.Single(x => x.Definition.Value == "race-odds");
            Assert.AreEqual(CollectionTaskStatus.Ready, detail.Status);
            Assert.AreEqual(HorseRacingPrediction.Contracts.Collection.CollectionDefinitionRevisions.RaceDetail,
                detail.RequestedRevision);
            Assert.AreEqual(CollectionTaskStatus.Ready, odds.Status);
            Assert.AreEqual($"{date:yyyyMMdd}:Tokyo:11", detail.Resource.Id);
            Assert.AreEqual(detail.Resource.Id, odds.Resource.Id);
            Assert.AreEqual(15, schedule.RequestedDates.Count); // the approved ±7 day discovery window
            Assert.HasCount(1, sessions.Navigator.RaceListRequests);

            using var rediscoveryResponse = await client.PostAsJsonAsync("api/v2/admin/collection/tasks", new
            {
                Task = new
                {
                    Mode = "Resource",
                    Resource = new
                    {
                        ResourceType = CollectionResourceType.Race,
                        Provider = "JRA",
                        ResourceId = detail.Resource.Id,
                        DefinitionId = "race-detail",
                        RequestedRevision = HorseRacingPrediction.Contracts.Collection.CollectionDefinitionRevisions.RaceDetail,
                        Reason = CollectionReason.Discovery,
                        Lane = CollectionLane.Realtime,
                        Priority = 80,
                        EffectiveDate = date,
                    }
                }
            });
            rediscoveryResponse.EnsureSuccessStatusCode();
            var responseBody = await rediscoveryResponse.Content.ReadFromJsonAsync<CreateCollectionTaskResponse>();
            var rediscovery = responseBody?.Submission;
            Assert.IsNotNull(rediscovery);
            Assert.AreEqual("Resource", rediscovery.Mode);
            Assert.IsFalse(rediscovery.Receipt.CreatedTask);
            Assert.AreEqual(detail.TaskId, rediscovery.Receipt.TaskId);
            Assert.HasCount(3, await store.GetTasksAsync(limit: 10));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task DiscoveryBatch_ReplaysSameParentSafelyAndRecoveryPreservesAcceptedChildReceipts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-discovery-replay-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            { StateDirectory = directory }));
            await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", CollectionResourceType.Race,
                1, "initial", false);
            await store.RegisterDefinitionAsync(new("race-detail"), "Race detail", CollectionResourceType.Race,
                HorseRacingPrediction.Contracts.Collection.CollectionDefinitionRevisions.RaceDetail,
                "artifact state machine", false);
            await store.RegisterDefinitionAsync(new("race-odds"), "Race odds", CollectionResourceType.RaceOdds,
                1, "initial", false);

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(store);
            builder.Services.AddSingleton<ICollectionDispatchTelemetry, NullCollectionDispatchTelemetry>();
            using var domain = new SqliteDbContextProvider();
            builder.Services.AddSingleton<IDbContextProvider<EventStoreDbContext>>(domain);
            await using var app = builder.Build();
            app.MapCollectionApiV2Endpoints();
            await app.StartAsync();
            using var client = app.GetTestClient();

            var date = new DateOnly(2026, 9, 12);
            var now = DateTimeOffset.UtcNow;
            var parentResource = new ResourceKey(CollectionResourceType.Race, "JRA", $"discovery:{date:yyyyMMdd}00");
            var originalReceipt = await store.RequestAsync(parentResource, new("race-discovery"), 1,
                CollectionReason.Discovery, now, CollectionLane.Realtime, 100, effectiveDate: date);
            var originalTaskId = originalReceipt.TaskId!.Value;
            var originalLease = await store.AcquireAsync(originalTaskId, 1, now, TimeSpan.FromMinutes(15));
            Assert.IsNotNull(originalLease);

            var race = new RaceId(date, RaceCourse.Tokyo, 11);
            var sessions = new DiscoverySessionFactory(date, race);
            var requestSink = new RecordingRequestSink(new CollectionRequestApiClient(client));
            var schedule = new ScheduleWorkflow(date);
            var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero));
            var handler = new JraRaceDiscoveryCollectionHandler(sessions,
                _ => schedule, requestSink, timeProvider: timeProvider);

            var first = await handler.CollectAsync(originalLease, CancellationToken.None);
            Assert.AreEqual(CollectionAttemptResult.Succeeded, first.Result);
            var firstOutcomes = requestSink.Responses.Single().Outcomes;
            Assert.IsTrue(firstOutcomes.All(outcome => outcome.Status == "Created"));
            var originalReceipts = firstOutcomes.Select(outcome => (outcome.ItemKey, outcome.RequestId, outcome.TaskId))
                .OrderBy(outcome => outcome.ItemKey, StringComparer.Ordinal).ToArray();
            var originalBatchId = requestSink.Requests[0].BatchId;
            Assert.IsTrue(originalBatchId.StartsWith($"race-discovery:v2:{originalTaskId:N}:c0:", StringComparison.Ordinal));

            var unchangedReplay = await handler.CollectAsync(originalLease, CancellationToken.None);
            Assert.AreEqual(CollectionAttemptResult.Succeeded, unchangedReplay.Result);
            var unchangedOutcomes = requestSink.Responses[1].Outcomes;
            Assert.IsTrue(unchangedOutcomes.All(outcome => outcome.Status == "Reused"));
            Assert.AreEqual(originalBatchId, requestSink.Requests[1].BatchId);
            CollectionAssert.AreEqual(originalReceipts,
                unchangedOutcomes.Select(outcome => (outcome.ItemKey, outcome.RequestId, outcome.TaskId))
                    .OrderBy(outcome => outcome.ItemKey, StringComparer.Ordinal).ToArray());

            var navigator = sessions.Navigator;
            var raceTaskId = originalReceipts.Single(receipt => receipt.ItemKey.StartsWith("race:", StringComparison.Ordinal))
                .TaskId!.Value;
            var terminalRaceTime = DateTimeOffset.UtcNow.AddMinutes(1);
            var terminalRaceLease = await store.AcquireAsync(raceTaskId, 1, terminalRaceTime, TimeSpan.FromMinutes(15));
            Assert.IsNotNull(terminalRaceLease);
            Assert.IsTrue(await store.CompleteAttemptAsync(raceTaskId, terminalRaceLease.LeaseToken,
                terminalRaceTime.AddSeconds(1), new(CollectionAttemptResult.Succeeded)));
            var terminalAttemptCount = (await store.GetTasksAsync(limit: 10))
                .Single(task => task.TaskId == raceTaskId).AttemptCount;

            navigator.StartTime = new TimeOnly(16, 0);
            var changedReplay = await handler.CollectAsync(originalLease, CancellationToken.None);
            Assert.AreEqual(CollectionAttemptResult.Succeeded, changedReplay.Result);
            Assert.AreNotEqual(originalBatchId, requestSink.Requests[2].BatchId);
            var changedOutcomes = requestSink.Responses[2].Outcomes;
            Assert.IsTrue(changedOutcomes.All(outcome => outcome.Status == "Reused"));
            CollectionAssert.AreEqual(originalReceipts,
                changedOutcomes.Select(outcome => (outcome.ItemKey, outcome.RequestId, outcome.TaskId))
                    .OrderBy(outcome => outcome.ItemKey, StringComparer.Ordinal).ToArray());
            var afterChangedReplay = await store.GetTasksAsync(limit: 10);
            Assert.HasCount(3, afterChangedReplay);
            Assert.AreEqual(CollectionTaskStatus.Succeeded,
                afterChangedReplay.Single(task => task.TaskId == raceTaskId).Status);
            Assert.AreEqual(terminalAttemptCount,
                afterChangedReplay.Single(task => task.TaskId == raceTaskId).AttemptCount,
                "Changed discovery content must reuse an already-terminal ordinary child without a new attempt.");

            navigator.AdditionalRace = new RaceId(date, RaceCourse.Tokyo, 12);
            var addedItemReplay = await handler.CollectAsync(originalLease, CancellationToken.None);
            Assert.AreEqual(CollectionAttemptResult.Succeeded, addedItemReplay.Result);
            Assert.AreNotEqual(requestSink.Requests[2].BatchId, requestSink.Requests[3].BatchId);
            var addedItemOutcomes = requestSink.Responses[3].Outcomes;
            Assert.AreEqual(3, addedItemOutcomes.Count);
            Assert.IsTrue(addedItemOutcomes.Where(outcome => originalReceipts.Any(receipt =>
                    receipt.ItemKey == outcome.ItemKey))
                .All(outcome => outcome.Status == "Reused"));
            CollectionAssert.AreEqual(originalReceipts,
                addedItemOutcomes.Where(outcome => originalReceipts.Any(receipt =>
                        receipt.ItemKey == outcome.ItemKey))
                    .Select(outcome => (outcome.ItemKey, outcome.RequestId, outcome.TaskId))
                    .OrderBy(outcome => outcome.ItemKey, StringComparer.Ordinal).ToArray());
            var addedRaceOutcome = addedItemOutcomes.Single(outcome => outcome.ItemKey == "race:20260912:Tokyo:12");
            Assert.AreEqual("Created", addedRaceOutcome.Status);
            Assert.IsNotNull(addedRaceOutcome.TaskId);
            Assert.IsFalse(originalReceipts.Any(receipt => receipt.TaskId == addedRaceOutcome.TaskId));
            Assert.HasCount(4, await store.GetTasksAsync(limit: 10));

            // Keep the store/API mismatch guard covered through the real handler completion/readback path.
            // Pinning the original caller identity is test-only; production uses the content-scoped identity above.
            navigator.AdditionalRace = null;
            navigator.StartTime = new TimeOnly(16, 30);
            requestSink.BatchIdOverride = originalBatchId;
            var changedReplayRejected = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                handler.CollectAsync(originalLease, CancellationToken.None));
            StringAssert.Contains(changedReplayRejected.Message, "race:20260912:Tokyo:11|Rejected|IdempotencyMismatch");
            StringAssert.Contains(changedReplayRejected.Message, "odds:20260912:Tokyo:11|Rejected|IdempotencyMismatch");
            Assert.HasCount(4, await store.GetTasksAsync(limit: 10),
                "A same-batch-ID content mismatch must not duplicate accepted children.");

            using (var failureResponse = await client.PostAsJsonAsync(
                       $"api/v2/internal/collection/tasks/{originalTaskId}/attempts",
                       new CompleteCollectionTaskAttemptRequest(new CompleteCollectionTaskAttemptInputDto(
                           originalLease.LeaseToken, CollectionAttemptResult.PermanentFailure,
                           "InvalidOperationException", changedReplayRejected.Message))
                       { Id = originalTaskId }))
                failureResponse.EnsureSuccessStatusCode();

            using var originalDetailResponse = await client.GetAsync(
                $"api/v2/admin/collection/resources/Race/JRA/{Uri.EscapeDataString(parentResource.Id)}/definitions/race-discovery?historyPageSize=10");
            Assert.AreEqual(System.Net.HttpStatusCode.OK, originalDetailResponse.StatusCode);
            var originalDetailBody = await originalDetailResponse.Content
                .ReadFromJsonAsync<GetCollectionResourceDetailResponse>();
            Assert.IsNotNull(originalDetailBody);
            Assert.IsTrue(originalDetailBody.Resource.Attempts.Any(attempt =>
                attempt.ErrorCode == "InvalidOperationException"
                && attempt.ErrorMessage == changedReplayRejected.Message));
            var failure = originalDetailBody.Resource.Failures!.Single();
            Assert.AreEqual("InvalidOperationException", failure.ErrorCode);
            Assert.AreEqual(changedReplayRejected.Message, failure.ErrorMessage);

            using var recoveryResponse = await client.PostAsJsonAsync("api/v2/admin/collection/recovery-batches",
                new CreateCollectionRecoveryBatchRequest(new CreateCollectionRecoveryBatchInputDto(
                    "NotificationIds", NotificationIds: [failure.NotificationId])));
            Assert.AreEqual(System.Net.HttpStatusCode.Accepted, recoveryResponse.StatusCode);
            var recoveryBody = await recoveryResponse.Content.ReadFromJsonAsync<CreateCollectionRecoveryBatchResponse>();
            Assert.IsNotNull(recoveryBody);
            Assert.AreEqual(1, recoveryBody.Recovery.SelectedCount);
            var recoveryTaskId = recoveryBody.Recovery.TaskIds.Single();

            navigator.StartTime = new TimeOnly(15, 30);
            navigator.CardUrl = "https://example.test/card/11";
            requestSink.BatchIdOverride = null;
            await store.SetPausedAsync(false, null, DateTimeOffset.UtcNow, CancellationToken.None);
            var recoveryHandler = new JraRaceDiscoveryCollectionHandler(sessions,
                _ => schedule, requestSink, timeProvider: timeProvider);
            await new CollectionPlatformWorkerClient(client,
                    new CollectionDefinitionHandlerRegistry([recoveryHandler]))
                .ExecuteAsync(new(recoveryTaskId, 1), CancellationToken.None);

            var recoveryOutcomes = requestSink.Responses.Last().Outcomes;
            Assert.IsTrue(recoveryOutcomes.All(outcome => outcome.Status == "Reused"));
            CollectionAssert.AreEqual(originalReceipts,
                recoveryOutcomes.Select(outcome => (outcome.ItemKey, outcome.RequestId, outcome.TaskId))
                    .OrderBy(outcome => outcome.ItemKey, StringComparer.Ordinal).ToArray());
            var finalTasks = await store.GetTasksAsync(limit: 10);
            Assert.HasCount(5, finalTasks);
            Assert.IsTrue(originalReceipts.All(receipt => finalTasks.Any(task => task.TaskId == receipt.TaskId)));
            Assert.IsTrue(finalTasks.Any(task => task.TaskId == addedRaceOutcome.TaskId));
            Assert.AreEqual(CollectionTaskStatus.Succeeded,
                finalTasks.Single(task => task.TaskId == recoveryTaskId).Status);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task FutureUnpublishedDiscovery_TraversesQueueAndProjectsAsWaitingWithoutFailureNotification()
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-discovery-waiting-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            { StateDirectory = directory }));
            await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", CollectionResourceType.Race,
                1, "initial", false);
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(store);
            builder.Services.AddSingleton<ICollectionDispatchTelemetry, NullCollectionDispatchTelemetry>();
            var app = builder.Build();
            app.MapCollectionApiV2Endpoints();
            await app.StartAsync();
            await using var appLifetime = app;
            using var client = app.GetTestClient();

            var today = new DateOnly(2026, 9, 12);
            var future = new DateOnly(2026, 9, 19);
            using var createResponse = await client.PostAsJsonAsync("api/v2/admin/collection/tasks", new
            {
                Task = new
                {
                    Mode = "Resource",
                    Resource = new
                    {
                        ResourceType = CollectionResourceType.Race,
                        Provider = "JRA",
                        ResourceId = $"discovery:{today:yyyyMMdd}",
                        DefinitionId = "race-discovery",
                        RequestedRevision = 1,
                        Reason = CollectionReason.Discovery,
                        Lane = CollectionLane.Realtime,
                        Priority = 100,
                        EffectiveDate = today,
                    }
                }
            });
            createResponse.EnsureSuccessStatusCode();

            var queue = new RecordingQueue();
            var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue,
                Options.Create(new CollectionQueueOptions
                {
                    Enabled = true,
                    DispatchBatchSize = 1,
                    AggregationDelayMilliseconds = 0
                }),
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance);
            await dispatcher.DispatchOnceAsync(CancellationToken.None);
            var envelope = JsonSerializer.Deserialize<CollectionDispatchEnvelope>(queue.MessageBodies.Single(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.IsNotNull(envelope);
            var notification = envelope.Tasks.Single();

            var schedule = new ScheduleWorkflow(future);
            var handler = new JraRaceDiscoveryCollectionHandler(new UnpublishedDiscoverySessionFactory(),
                _ => schedule, new CollectionRequestApiClient(client),
                Options.Create(new RaceDiscoveryCollectionOptions()),
                new FixedTimeProvider(new(2026, 9, 12, 1, 0, 0, TimeSpan.Zero)));
            await new CollectionPlatformWorkerClient(client, new CollectionDefinitionHandlerRegistry([handler]))
                .ExecuteAsync(new(notification.TaskId, notification.DispatchGeneration), CancellationToken.None);

            var tasksResponse = await client.GetFromJsonAsync<ListCollectionTasksResponse>(
                "api/v2/admin/collection/tasks?statuses=Ready");
            var tasks = tasksResponse?.Page;
            var statesResponse = await client.GetFromJsonAsync<SearchCollectionStatesResponse>(
                "api/v2/admin/collection/states?statuses=Pending");
            var states = statesResponse?.Page;
            var failuresResponse = await client.GetFromJsonAsync<ListFailureNotificationsResponse>(
                "api/v2/admin/collection/failure-notifications?view=Actionable");
            var failures = failuresResponse?.Notifications;
            Assert.IsNotNull(tasks);
            Assert.HasCount(1, tasks.Items);
            Assert.IsGreaterThan(new DateTimeOffset(2026, 9, 12, 1, 0, 0, TimeSpan.Zero),
                tasks.Items[0].AvailableAt);
            Assert.IsNotNull(states);
            Assert.HasCount(1, states.Items);
            var nextCollectionAt = states.Items[0].NextCollectionAt;
            Assert.IsTrue(nextCollectionAt.HasValue);
            Assert.IsGreaterThan(new DateTimeOffset(2026, 9, 12, 1, 0, 0, TimeSpan.Zero),
                nextCollectionAt.GetValueOrDefault());
            Assert.IsEmpty(failures!);
            var attempt = (await store.GetAttemptsAsync(tasks.Items[0].TaskId)).Single();
            Assert.AreEqual(CollectionAttemptResult.ResourceNotYetAvailable, attempt.Result);
            Assert.AreEqual("RaceListNotYetAvailable", attempt.ErrorCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RecordingQueue : ICollectionPlatformTaskQueue
    {
        public List<string> MessageBodies { get; } = [];

        public Task<CollectionQueueSendReceipt> SendAsync(CollectionDispatchEnvelope envelope,
            CancellationToken cancellationToken)
        {
            MessageBodies.Add(JsonSerializer.Serialize(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            return Task.FromResult(new CollectionQueueSendReceipt(Guid.NewGuid().ToString("N")));
        }
    }

    private sealed class ScheduleWorkflow(DateOnly raceDate) : IJraScheduleCollectionWorkflow
    {
        public List<DateOnly> RequestedDates { get; } = [];

        public Task<IReadOnlyList<RaceCourse>> CollectAsync(DateOnly date,
            CancellationToken cancellationToken = default)
        {
            RequestedDates.Add(date);
            return Task.FromResult<IReadOnlyList<RaceCourse>>(date == raceDate ? [RaceCourse.Tokyo] : []);
        }
    }

    private sealed class DiscoverySessionFactory(DateOnly date, RaceId race) : IJraSessionFactory
    {
        public DiscoveryNavigator Navigator { get; } = new(date, race);

        public Task<JraSession> CreateAsync(CancellationToken cancellationToken = default)
        {
            var browser = new NoOpBrowser();
            return Task.FromResult(new JraSession(browser, Navigator,
                new JraPageReader(browser, Array.Empty<IJraPageParser>())));
        }
    }

    private sealed class UnpublishedDiscoverySessionFactory : IJraSessionFactory
    {
        public Task<JraSession> CreateAsync(CancellationToken cancellationToken = default)
        {
            var browser = new NoOpBrowser();
            return Task.FromResult(new JraSession(browser, new UnpublishedDiscoveryNavigator(),
                new JraPageReader(browser, Array.Empty<IJraPageParser>())));
        }
    }

    private sealed class UnpublishedDiscoveryNavigator : IJraNavigator
    {
        public Task<IJraPage> ToRaceListAsync(DateOnly date, RaceCourse course,
            CancellationToken cancellationToken = default) => throw new JraNavigationException(
            "not published", JraNavigationFailureReason.NotYetPublished);
        public Task<IJraPage> ToKeibaTopAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IJraPage> ToCalendarAsync(YearMonth month, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IJraPage> ToRaceCardAsync(RaceId race, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IJraPage> ToRaceResultAsync(RaceId race, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IJraPage> ToRaceResultListAsync(DateOnly date, RaceCourse course, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IJraPage> ToHistoricalRaceSearchAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public bool IsWithinRaceCardLookupPeriod(DateOnly date) => true;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class DiscoveryNavigator(DateOnly date, RaceId race) : IJraNavigator
    {
        public List<(DateOnly Date, RaceCourse Course)> RaceListRequests { get; } = [];
        public TimeOnly StartTime { get; set; } = new(15, 30);
        public string CardUrl { get; set; } = "https://example.test/card/11";
        public RaceId? AdditionalRace { get; set; }

        public Task<IJraPage> ToRaceListAsync(DateOnly requestedDate, RaceCourse course,
            CancellationToken cancellationToken = default)
        {
            RaceListRequests.Add((requestedDate, course));
            var races = new List<RaceSummary>
            {
                new(race, "E2E race", StartTime, CardUrl, "https://example.test/result/11"),
            };
            if (AdditionalRace is { } additionalRace)
                races.Add(new(additionalRace, "E2E added race", null, null, null));
            return Task.FromResult<IJraPage>(new JraRaceListPage("https://example.test/races", date,
                RaceCourse.Tokyo, races));
        }

        public Task<IJraPage> ToKeibaTopAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IJraPage> ToCalendarAsync(YearMonth month, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IJraPage> ToRaceCardAsync(RaceId raceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IJraPage> ToRaceResultAsync(RaceId raceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IJraPage> ToRaceResultListAsync(DateOnly requestedDate, RaceCourse course, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IJraPage> ToHistoricalRaceSearchAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public bool IsWithinRaceCardLookupPeriod(DateOnly requestedDate) => true;
    }

    private sealed class NoOpBrowser : IWebBrowser
    {
        public string? CurrentUrl => null;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<string> NavigateAsync(string url, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> ClickAsync(string text, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> SelectOptionAsync(string fieldText, string optionText, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> ClickActionInSectionAsync(string sectionText, string actionText, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GetPageContentAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HorseRacingPrediction.Scraping.Browser.Snapshots.PageSnapshot> GetPageSnapshotAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PageLinkSnapshot>> GetLinksAsync(int maxResults = 0, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> SearchAsync(string query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GoBackAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingRequestSink(ICollectionRequestSink inner) : ICollectionRequestSink
    {
        public List<CollectionRequestBulkResponse> Responses { get; } = [];
        public List<CollectionRequestBulkRequest> Requests { get; } = [];
        public string? BatchIdOverride { get; set; }

        public Task RequestAsync(ResourceKey resource, CollectionDefinitionId definition, int requestedRevision,
            CollectionReason reason, CollectionLane lane, int priority, Uri? explicitUrl, DateOnly effectiveDate,
            IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken)
            => inner.RequestAsync(resource, definition, requestedRevision, reason, lane, priority, explicitUrl,
                effectiveDate, attributes, cancellationToken);

        public async Task<CollectionRequestBulkResponse> RequestManyAsync(CollectionRequestBulkRequest request,
            CancellationToken cancellationToken)
        {
            var submitted = BatchIdOverride is { } batchId ? request with { BatchId = batchId } : request;
            Requests.Add(submitted);
            var response = await inner.RequestManyAsync(submitted, cancellationToken);
            Responses.Add(response);
            return response;
        }
    }
}
