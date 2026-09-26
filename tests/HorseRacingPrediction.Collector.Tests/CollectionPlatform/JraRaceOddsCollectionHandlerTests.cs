using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Collector.Tests.TestSupport;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class JraRaceOddsCollectionHandlerTests
{
    [TestMethod]
    [DataRow(409, "RaceAssignmentNotConfirmed", CollectionAttemptResult.ResourceNotYetAvailable)]
    [DataRow(400, "UnknownHorseNumber", CollectionAttemptResult.ValidationFailure)]
    public async Task ApiRejection_KnownAssignmentErrorsStayLocal(
        int status, string errorCode, CollectionAttemptResult expectedResult)
    {
        var now = new DateTimeOffset(2026, 9, 12, 5, 0, 0, TimeSpan.Zero);
        var handler = CreateApiBackedHandler((HttpStatusCode)status,
            $"{{\"errorCode\":\"{errorCode}\"}}", now);

        var result = await handler.CollectAsync(CreateOddsTask(), CancellationToken.None);

        Assert.AreEqual(expectedResult, result.Result);
        Assert.AreEqual(errorCode, result.ErrorCode);
        Assert.AreEqual(CollectionFailureImpact.Isolated, result.FailureImpact);
        Assert.AreEqual(expectedResult == CollectionAttemptResult.ResourceNotYetAvailable
            ? now.AddMinutes(15) : null, result.RetryAt);
    }

    [TestMethod]
    [DataRow(400, "OtherValidationError")]
    [DataRow(409, "OtherConflict")]
    [DataRow(500, "RaceAssignmentNotConfirmed")]
    [DataRow(400, "RaceAssignmentNotConfirmed")]
    public async Task ApiRejection_UnknownOrWrongStatusIsNotIsolated(int status, string errorCode)
    {
        var handler = CreateApiBackedHandler((HttpStatusCode)status,
            $"{{\"errorCode\":\"{errorCode}\"}}",
            new DateTimeOffset(2026, 9, 12, 5, 0, 0, TimeSpan.Zero));

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            handler.CollectAsync(CreateOddsTask(), CancellationToken.None));
    }

    private static JraRaceOddsCollectionHandler CreateApiBackedHandler(
        HttpStatusCode status, string body, DateTimeOffset now)
    {
        var race = new RaceId(new DateOnly(2026, 9, 12), RaceCourse.Tokyo, 11);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceOddsFactory = _ => new JraRaceOddsPage("https://example.test/odds/11", race,
                    now, [new(1, 2.5m, 1)]),
            },
        };
        var client = new HttpClient(new StaticResponseHandler(status, body))
        {
            BaseAddress = new Uri("https://example.test/"),
        };
        return new JraRaceOddsCollectionHandler(sessions, new RaceOddsSnapshotApiClient(client),
            Options.Create(new RaceOddsCollectionOptions()), new FixedTimeProvider(now));
    }

    private static LeasedCollectionTask CreateOddsTask() => new(Guid.NewGuid(), Guid.NewGuid(),
        new(ResourceType.RaceOdds, "JRA", "20260912:Tokyo:11"), new("race-odds"), 1,
        CollectionReason.Discovery, CollectionLane.Realtime, 90, "lease",
        DateTimeOffset.UtcNow.AddMinutes(5), new DateOnly(2026, 9, 12),
        new Dictionary<string, string> { ["course"] = "東京", ["number"] = "11" });

    private sealed class StaticResponseHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [TestMethod]
    public async Task RepeatedCollection_AppendsDistinctObservedSnapshotsAndSchedulesNextObservation()
    {
        var date = new DateOnly(2026, 9, 12);
        var race = new RaceId(date, RaceCourse.Tokyo, 11);
        var calls = 0;
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceOddsFactory = _ =>
                {
                    calls++;
                    return new JraRaceOddsPage("https://example.test/odds/11", race,
                        new DateTimeOffset(2026, 9, 12, 5, calls, 0, TimeSpan.Zero),
                        [new(1, calls == 1 ? 2.5m : 2.3m, 1)]);
                },
            },
        };
        var sink = new RecordingSink();
        var handler = new JraRaceOddsCollectionHandler(sessions, sink,
            Options.Create(new RaceOddsCollectionOptions { EarlyIntervalMinutes = 7 }));
        var task = new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.RaceOdds, "JRA", "20260912:Tokyo:11"), new("race-odds"), 1,
            CollectionReason.Discovery, CollectionLane.Realtime, 90, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), date,
            new Dictionary<string, string> { ["course"] = "東京", ["number"] = "11", ["startTime"] = "15:00" });

        var first = await handler.CollectAsync(task, CancellationToken.None);
        var second = await handler.CollectAsync(task with { TaskId = Guid.NewGuid() }, CancellationToken.None);

        Assert.HasCount(2, sink.Pages);
        Assert.AreNotEqual(sink.Pages[0].ObservedAt, sink.Pages[1].ObservedAt);
        Assert.AreEqual(2.5m, sink.Pages[0].Entries[0].WinOdds);
        Assert.AreEqual(2.3m, sink.Pages[1].Entries[0].WinOdds);
        Assert.IsNotNull(first.NextCollectionAt);
        Assert.IsNotNull(second.NextCollectionAt);
    }

    private sealed class RecordingSink : IRaceOddsSnapshotSink
    {
        public List<JraRaceOddsPage> Pages { get; } = [];
        public Task SaveAsync(string raceId, JraRaceOddsPage page, CancellationToken cancellationToken)
        { Pages.Add(page); return Task.CompletedTask; }
    }
}
