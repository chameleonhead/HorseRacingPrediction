using System.Net.Http.Json;
using System.Text.Json;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using HorseRacingPrediction.Scraping.Jra.Workflow;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

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
            await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", ResourceType.Race,
                1, "initial", false);
            await store.RegisterDefinitionAsync(new("race-card"), "Race card", ResourceType.RaceCard,
                1, "initial", false);
            await store.RegisterDefinitionAsync(new("race-result"), "Race result", ResourceType.RaceResult,
                1, "initial", false);
            await store.RegisterDefinitionAsync(new("race-odds"), "Race odds", ResourceType.RaceOdds,
                1, "initial", false);

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(store);
            var app = builder.Build();
            app.MapCollectionPlatformEndpoints();
            await app.StartAsync();
            await using var appLifetime = app;
            using var client = app.GetTestClient();

            var date = new DateOnly(2026, 9, 12);
            using var createResponse = await client.PostAsJsonAsync("api/admin/collection/requests", new
            {
                ResourceType = ResourceType.Race,
                Provider = "JRA",
                ResourceId = $"discovery:{date:yyyyMMdd}",
                DefinitionId = "race-discovery",
                RequestedRevision = 1,
                Reason = CollectionReason.Discovery,
                Lane = CollectionLane.Realtime,
                Priority = 100,
                EffectiveDate = date,
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
            var handler = new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, requestSink);
            var worker = new CollectionPlatformWorkerClient(client,
                new CollectionDefinitionHandlerRegistry([handler]));

            await worker.ExecuteAsync(new(notification.TaskId, notification.DispatchGeneration), CancellationToken.None);

            var tasks = await store.GetTasksAsync(limit: 10);
            var discovery = tasks.Single(x => x.Definition.Value == "race-discovery");
            Assert.AreEqual(CollectionTaskStatus.Succeeded, discovery.Status);
            var card = tasks.Single(x => x.Definition.Value == "race-card");
            var result = tasks.Single(x => x.Definition.Value == "race-result");
            var odds = tasks.Single(x => x.Definition.Value == "race-odds");
            Assert.AreEqual(CollectionTaskStatus.Ready, card.Status);
            Assert.AreEqual(CollectionTaskStatus.Ready, result.Status);
            Assert.AreEqual(CollectionTaskStatus.Ready, odds.Status);
            Assert.AreEqual($"{date:yyyyMMdd}:Tokyo:11", card.Resource.Id);
            Assert.AreEqual(card.Resource.Id, result.Resource.Id);
            Assert.AreEqual(card.Resource.Id, odds.Resource.Id);
            Assert.AreEqual(15, schedule.RequestedDates.Count); // the approved ±7 day discovery window
            Assert.HasCount(1, sessions.Navigator.RaceListRequests);
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
            await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", ResourceType.Race,
                1, "initial", false);
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(store);
            var app = builder.Build();
            app.MapCollectionPlatformEndpoints();
            await app.StartAsync();
            await using var appLifetime = app;
            using var client = app.GetTestClient();

            var today = new DateOnly(2026, 9, 12);
            var future = new DateOnly(2026, 9, 19);
            using var createResponse = await client.PostAsJsonAsync("api/admin/collection/requests", new
            {
                ResourceType = ResourceType.Race,
                Provider = "JRA",
                ResourceId = $"discovery:{today:yyyyMMdd}",
                DefinitionId = "race-discovery",
                RequestedRevision = 1,
                Reason = CollectionReason.Discovery,
                Lane = CollectionLane.Realtime,
                Priority = 100,
                EffectiveDate = today,
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

            var tasks = await client.GetFromJsonAsync<CollectionTaskPage>(
                "api/admin/collection/tasks/search?statuses=Ready");
            var states = await client.GetFromJsonAsync<CollectionStatePage>(
                "api/admin/collection/states/search?statuses=Pending");
            var failures = await client.GetFromJsonAsync<IReadOnlyList<PendingCollectionFailureNotification>>(
                "api/admin/collection/failure-notifications");
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

        public Task<IJraPage> ToRaceListAsync(DateOnly requestedDate, RaceCourse course,
            CancellationToken cancellationToken = default)
        {
            RaceListRequests.Add((requestedDate, course));
            return Task.FromResult<IJraPage>(new JraRaceListPage("https://example.test/races", date,
                RaceCourse.Tokyo,
                [new RaceSummary(race, "E2E race", new TimeOnly(15, 30),
                    "https://example.test/card/11", "https://example.test/result/11")]));
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
}
