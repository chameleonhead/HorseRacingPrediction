using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Collector.Tests.TestSupport;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Pages;
using Microsoft.Extensions.Options;
using System.Globalization;

using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class JraRaceDiscoveryCollectionHandlerTests
{
    [TestMethod]
    public void DiscoveryBatchId_IsContentScopedCanonicalAndPreservesExcludedParentIdentity()
    {
        var date = new DateOnly(2026, 9, 12);
        var task = CreateDiscoveryTask(date) with
        { TaskId = Guid.Parse("11111111-1111-1111-1111-111111111111") };
        var item = CreateDiscoveryBatchItem(date);
        var additionalItem = item with
        {
            ItemKey = "race:20260912:Tokyo:12",
            ResourceId = "20260912:Tokyo:12",
            Attributes = new Dictionary<string, string>
            {
                ["number"] = "12",
                ["course"] = "東京",
            },
        };
        var reorderedAttributes = item with
        {
            Attributes = new Dictionary<string, string>
            {
                ["startTime"] = "15:30",
                ["number"] = "11",
                ["course"] = "東京",
            },
        };
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        string baseline;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            baseline = JraRaceDiscoveryCollectionHandler.CreateDiscoveryBatchId(task, 3,
                [item, additionalItem]);
            Assert.AreEqual(baseline, JraRaceDiscoveryCollectionHandler.CreateDiscoveryBatchId(task, 3,
                [additionalItem, reorderedAttributes]));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }

        Assert.AreEqual(baseline, JraRaceDiscoveryCollectionHandler.CreateDiscoveryBatchId(task, 3,
            [additionalItem, reorderedAttributes]),
            "Content identity must be independent of the process culture.");
        Assert.IsTrue(baseline.StartsWith($"race-discovery:v2:{task.TaskId:N}:c3:", StringComparison.Ordinal));
        Assert.IsLessThanOrEqualTo(128, baseline.Length);
        Assert.AreEqual(64, baseline.Split(':')[4].Length);

        var changedRequestFields = new CollectionRequestBulkItemDto[]
        {
            item with { ItemKey = "race:20260912:Tokyo:12" },
            item with { ResourceType = "RaceOdds" },
            item with { Provider = "OTHER" },
            item with { ResourceId = "20260912:Tokyo:12" },
            item with { DefinitionId = "other-definition" },
            item with { RequestedRevision = item.RequestedRevision + 1 },
            item with { Reason = CollectionReason.ManualRefresh.ToString() },
            item with { Lane = CollectionLane.Background.ToString() },
            item with { Priority = item.Priority + 1 },
            item with { ExplicitUrl = "https://example.test/card/12" },
            item with { EffectiveDate = date.AddDays(1) },
            item with
            {
                Attributes = new Dictionary<string, string>
                {
                    ["course"] = "東京",
                    ["number"] = "11",
                    ["startTime"] = "16:00",
                },
            },
        };
        foreach (var changedItem in changedRequestFields)
            Assert.AreNotEqual(JraRaceDiscoveryCollectionHandler.ComputeDiscoveryBatchContentDigest([item]),
                JraRaceDiscoveryCollectionHandler.ComputeDiscoveryBatchContentDigest([changedItem]),
                $"A changed request-affecting field must change the content digest: {changedItem.ItemKey}");

        var legacyBatchId = $"race-discovery:{task.TaskId:N}:c3";
        var oddsOnlyDiscoveryChunk = new[] { item with { ItemKey = "odds:20260912:Tokyo:11" } };
        foreach (var excludedParentReason in new[] { CollectionReason.Backfill, CollectionReason.PeriodRecollection })
        {
            var excludedParent = task with { Reason = excludedParentReason };
            Assert.AreEqual(legacyBatchId,
                JraRaceDiscoveryCollectionHandler.CreateDiscoveryBatchId(excludedParent, 3, oddsOnlyDiscoveryChunk));
        }
        Assert.AreEqual(legacyBatchId,
            JraRaceDiscoveryCollectionHandler.CreateDiscoveryBatchId(task, 3,
                [item with { Reason = CollectionReason.Backfill.ToString() }]));
    }

    [TestMethod]
    [TestCategory("External")]
    public async Task OfficialCancelledDay_RealScheduleNavigatorParserAndHandler_ContinueHanshin()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var sessions = new JraSessionFactory(new LiveBrowserFactory(), [
            new HorseRacingPrediction.Scraping.Jra.Parsing.CalendarPageParser(),
            new HorseRacingPrediction.Scraping.Jra.Parsing.RaceListPageParser(),
            new HorseRacingPrediction.Scraping.Jra.Parsing.RaceResultPageParser()]);
        var sink = new RecordingSink();
        var handler = new JraRaceDiscoveryCollectionHandler(sessions,
            session => new HorseRacingPrediction.Scraping.Jra.Workflow.JraScheduleCollectionWorkflow(session), sink,
            timeProvider: new FixedTimeProvider(new(2026, 9, 24, 0, 0, 0, TimeSpan.Zero)));
        var result = await handler.CollectAsync(CreateDiscoveryTask(new(2026, 9, 21))
            with
        { Reason = CollectionReason.Backfill }, cts.Token);
        Assert.AreEqual(CollectionAttemptResult.Succeeded, result.Result);
        Assert.HasCount(12, sink.Requests);
        Assert.IsTrue(sink.Requests.All(x => x.Resource.Id.StartsWith("20260921:Hanshin:", StringComparison.Ordinal)));
        StringAssert.Contains(result.PageIdentification!, "Cancelled=20260921:Nakayama:4:7");
    }

    private sealed class LiveBrowserFactory : HorseRacingPrediction.Scraping.Browser.IWebBrowserSessionFactory
    {
        public async Task<HorseRacingPrediction.Scraping.Browser.IWebBrowser> CreateAsync(CancellationToken cancellationToken = default)
            => await HorseRacingPrediction.Scraping.Browser.PlaywrightWebBrowser.CreateAsync();
    }

    [TestMethod]
    [DataRow(24)]
    [DataRow(30)]
    public async Task CancelledMeeting_DoesNotBlockOtherCourseOrReplacementDate(int todayDay)
    {
        var date = new DateOnly(2026, 9, 21);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (_, _) => throw new JraNavigationException("retired", JraNavigationFailureReason.OutOfDisplayedRange),
                RaceResultListFactory = (target, course) => target == date && course == RaceCourse.Nakayama
                    ? throw new JraNavigationException("absent", JraNavigationFailureReason.OutOfDisplayedRange)
                    : new JraRaceListPage("https://example.test/results", target, course, [new(new(target, course, 1), "race", null, null, null)]),
                MeetingCancellationFactory = (target, course) => new(target, course, 4, 7,
                    new("https://www.jra.go.jp/keiba/calendar2026/2026/9/0921.html")),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        {
            CoursesByDate = target => target == date
            ? [RaceCourse.Nakayama, RaceCourse.Hanshin] : target == date.AddDays(1) ? [RaceCourse.Nakayama] : []
        };
        var sink = new RecordingSink();
        var handler = new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, sink,
            timeProvider: new FixedTimeProvider(new(2026, 9, todayDay, 0, 0, 0, TimeSpan.Zero)));
        var result = await handler.CollectAsync(CreateDiscoveryTask(date), CancellationToken.None);
        Assert.AreEqual(CollectionAttemptResult.Succeeded, result.Result);
        CollectionAssert.AreEquivalent(new[] { "20260921:Hanshin:1", "20260922:Nakayama:1" }, sink.Requests.Select(x => x.Resource.Id).ToArray());
        StringAssert.Contains(result.PageIdentification!, "Cancelled=20260921:Nakayama:4:7");
        StringAssert.Contains(result.PageIdentification!, "CancelledMeetings=1");
    }

    [TestMethod]
    public async Task CancelledThenUnknownMeeting_PreservesFailureAndEarlierOfficialEvidence()
    {
        var date = new DateOnly(2026, 9, 21);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (_, _) => throw new JraNavigationException("absent", JraNavigationFailureReason.OutOfDisplayedRange),
                RaceResultListFactory = (_, _) => throw new JraNavigationException("absent", JraNavigationFailureReason.OutOfDisplayedRange),
                MeetingCancellationFactory = (target, course) => course == RaceCourse.Nakayama
                    ? new(target, course, 4, 7, new("https://www.jra.go.jp/keiba/calendar2026/2026/9/0921.html")) : null
            }
        };
        var schedule = new FakeJraScheduleCollectionWorkflow { CoursesByDate = target => target == date ? [RaceCourse.Nakayama, RaceCourse.Hanshin] : [] };
        var handler = new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, new RecordingSink(),
            timeProvider: new FixedTimeProvider(new(2026, 9, 24, 0, 0, 0, TimeSpan.Zero)));
        var result = await handler.CollectAsync(CreateDiscoveryTask(date), CancellationToken.None);
        Assert.AreEqual(CollectionAttemptResult.PermanentFailure, result.Result);
        Assert.AreEqual(nameof(JraNavigationException), result.ErrorCode);
        StringAssert.Contains(result.PageIdentification!, "Cancelled=20260921:Nakayama:4:7");
        StringAssert.Contains(result.PageIdentification!, "Source=https://www.jra.go.jp/");
    }

    [TestMethod]
    [DataRow("cancelled")]
    [DataRow("unknown")]
    [DataRow("wrong-date")]
    [DataRow("network")]
    public async Task CancellationEvidence_IsRequired_AndNoRaceSuccessIsInvented(string mode)
    {
        var date = new DateOnly(2026, 9, 21);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (_, _) => throw new JraNavigationException("missing", JraNavigationFailureReason.OutOfDisplayedRange),
                RaceResultListFactory = (_, _) => throw new JraNavigationException("missing", JraNavigationFailureReason.OutOfDisplayedRange),
                MeetingCancellationFactory = (target, course) => mode switch
                {
                    "unknown" => null,
                    "network" => throw new HttpRequestException("unavailable"),
                    _ => new(target.AddDays(mode == "wrong-date" ? 1 : 0), course, 4, 7,
                        new("https://www.jra.go.jp/keiba/calendar2026/2026/9/0921.html"))
                }
            }
        };
        var schedule = new FakeJraScheduleCollectionWorkflow { CoursesByDate = target => target == date ? [RaceCourse.Nakayama] : [] };
        var sink = new RecordingSink();
        var handler = new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, sink,
            timeProvider: new FixedTimeProvider(new(2026, 9, 24, 0, 0, 0, TimeSpan.Zero)));
        if (mode == "cancelled")
        {
            var result = await handler.CollectAsync(CreateDiscoveryTask(date), CancellationToken.None);
            Assert.AreEqual(CollectionAttemptResult.NotApplicable, result.Result);
            StringAssert.Contains(result.PageIdentification!, "CollectedMeetings=0");
        }
        else if (mode == "network")
            await Assert.ThrowsAsync<HttpRequestException>(() => handler.CollectAsync(CreateDiscoveryTask(date), CancellationToken.None));
        else
            await Assert.ThrowsAsync<JraNavigationException>(() => handler.CollectAsync(CreateDiscoveryTask(date), CancellationToken.None));
        Assert.HasCount(0, sink.Requests);
    }

    [TestMethod]
    [DataRow("2026-09-21")]
    [DataRow("2026-09-23")]
    public async Task Discovery_UsesOfficialScheduleEvidenceRegardlessOfWeekday(string dateText)
    {
        var date = DateOnly.Parse(dateText);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (target, course) => new JraRaceListPage("https://example.test/list",
                    target, course, [new(new(target, course, 1), "test", new(10, 0),
                        "https://example.test/card/1", "https://example.test/result/1")]),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        { CoursesByDate = target => target == date ? [RaceCourse.Nakayama] : [] };
        var sink = new RecordingSink();
        var handler = new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, sink,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero)));

        var result = await handler.CollectAsync(CreateDiscoveryTask(date), CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, result.Result);
        Assert.IsTrue(sink.Requests.Any(x => x.Resource.Type == CollectionResourceType.Race
            && x.Resource.Id == $"{date:yyyyMMdd}:Nakayama:1"));
        Assert.IsTrue(sink.Requests.Where(x => x.Definition.Value == "race-detail").All(x => x.RequestedRevision ==
            HorseRacingPrediction.Contracts.Collection.CollectionDefinitionRevisions.RaceDetail));
    }

    [TestMethod]
    public async Task Discovery_ExpandsActualRaceListIntoNewPlatformRequests()
    {
        var date = new DateOnly(2026, 9, 12);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (target, course) => new JraRaceListPage("https://example.test/list", target,
                    course, [new(new(target, course, 11), "test", new(15, 30),
                        "https://example.test/card/11", "https://example.test/result/11")]),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        {
            CoursesByDate = target => target == date ? [RaceCourse.Tokyo] : [],
        };
        var sink = new RecordingSink();
        var handler = new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, sink,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero)));
        var task = new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(CollectionResourceType.Race, "JRA", "discovery:2026091200"), new("race-discovery"), 1,
            CollectionReason.Discovery, CollectionLane.Realtime, 70, "lease", DateTimeOffset.UtcNow.AddMinutes(5),
            date, new Dictionary<string, string>());

        var result = await handler.CollectAsync(task, CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, result.Result);
        Assert.HasCount(2, sink.Requests);
        Assert.IsTrue(sink.Requests.Any(x => x.Resource.Type == CollectionResourceType.Race
            && x.Definition == new CollectionDefinitionId("race-detail")));
        Assert.IsTrue(sink.Requests.Any(x => x.Resource.Type == CollectionResourceType.RaceOdds));
        Assert.IsTrue(sink.Requests.All(x => x.Resource.Id == "20260912:Tokyo:11"));
        Assert.AreEqual("15:30", sink.Requests.Single(x => x.Resource.Type == CollectionResourceType.Race).Attributes["startTime"]);
        Assert.IsTrue(sink.Requests.All(x => x.Reason == CollectionReason.Discovery));
    }

    [TestMethod]
    [DataRow(250, new[] { 500 })]
    [DataRow(251, new[] { 500, 1 })]
    public async Task Discovery_BatchesCombinedRaceAndOddsItemsAtThe500ItemBoundary(
        int raceCount, int[] expectedBatchSizes)
    {
        var referenceDate = new DateOnly(2026, 9, 12);
        var meetings = Enumerable.Range(-4, 12)
            .SelectMany(offset => new[] { RaceCourse.Tokyo, RaceCourse.Nakayama }
                .Select(course => (Date: referenceDate.AddDays(offset), Course: course)))
            .Take((raceCount + 11) / 12)
            .ToArray();
        var raceRows = new Dictionary<(DateOnly Date, RaceCourse Course), IReadOnlyList<RaceSummary>>();
        var remaining = raceCount;
        var raceOrdinal = 0;
        foreach (var meeting in meetings)
        {
            var meetingRaceCount = Math.Min(12, remaining);
            raceRows[(meeting.Date, meeting.Course)] = Enumerable.Range(1, meetingRaceCount)
                .Select(number =>
                {
                    var ordinal = raceOrdinal++;
                    return new RaceSummary(new RaceId(meeting.Date, meeting.Course, number), "race",
                        ordinal < 250 ? new TimeOnly(15, 0) : null, null, null);
                }).ToArray();
            remaining -= meetingRaceCount;
        }

        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (date, course) => new JraRaceListPage(
                    "https://example.test/list", date, course, raceRows[(date, course)]),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        {
            CoursesByDate = date => meetings.Where(x => x.Date == date).Select(x => x.Course).ToArray(),
        };
        var sink = new RecordingBatchSink();
        var task = CreateDiscoveryTask(referenceDate) with
        {
            Attributes = new Dictionary<string, string> { ["batchId"] = "discovery-run-42" },
        };

        var result = await new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, sink,
                timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero)))
            .CollectAsync(task, CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, result.Result);
        CollectionAssert.AreEqual(expectedBatchSizes, sink.Batches.Select(x => x.Request.Items.Count).ToArray());
        Assert.IsEmpty(sink.SingleRequests);
        var firstRun = sink.Batches.SelectMany(x => x.Request.Items).ToArray();
        Assert.AreEqual(raceCount + 250, firstRun.Length);
        Assert.AreEqual(firstRun.Length, firstRun.Select(x => x.ItemKey).Distinct(StringComparer.Ordinal).Count());
        foreach (var raceItem in firstRun.Where(x => x.ItemKey.StartsWith("race:", StringComparison.Ordinal)))
        {
            var oddsItem = firstRun.SingleOrDefault(x => x.ItemKey == "odds:" + raceItem.ItemKey[5..]);
            if (oddsItem is null) continue;
            Assert.AreEqual(raceItem.Provider, oddsItem.Provider);
            Assert.AreEqual(raceItem.ResourceId, oddsItem.ResourceId);
            Assert.AreEqual(raceItem.EffectiveDate, oddsItem.EffectiveDate);
            Assert.AreEqual(raceItem.Attributes!["batchId"], oddsItem.Attributes!["batchId"]);
            Assert.AreEqual(raceItem.Attributes["course"], oddsItem.Attributes["course"]);
            Assert.AreEqual(raceItem.Attributes["number"], oddsItem.Attributes["number"]);
            Assert.AreEqual(raceItem.Attributes["startTime"], oddsItem.Attributes["startTime"]);
        }

        var firstBatchIds = sink.Batches.Select(x => x.Request.BatchId).ToArray();
        sink.Batches.Clear();
        await new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, sink,
                timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero)))
            .CollectAsync(task, CancellationToken.None);
        CollectionAssert.AreEqual(firstBatchIds, sink.Batches.Select(x => x.Request.BatchId).ToArray());
        CollectionAssert.AreEqual(firstRun.Select(x => x.ItemKey).ToArray(),
            sink.Batches.SelectMany(x => x.Request.Items).Select(x => x.ItemKey).ToArray());
    }

    [TestMethod]
    public async Task Discovery_RejectsBatchWithPerItemFailureOrMissingOutcome()
    {
        var date = new DateOnly(2026, 9, 12);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (target, course) => new JraRaceListPage(
                    "https://example.test/list", target, course,
                    [new(new(target, course, 1), "race", new(15, 0), null, null)]),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        { CoursesByDate = target => target == date ? [RaceCourse.Tokyo] : [] };

        foreach (var mode in new[]
                 {
                     "rejected", "missing", "invalid-created", "invalid-created-unknown-code", "invalid-held",
                     "invalid-accepted",
                 })
        {
            var sink = new RecordingBatchSink(mode);
            var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, sink,
                        timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero)))
                    .CollectAsync(CreateDiscoveryTask(date), CancellationToken.None));
            StringAssert.Contains(exception.Message, "Race discovery batch response rejected");
            if (mode == "rejected")
            {
                StringAssert.Contains(exception.Message, "race:20260912:Tokyo:1");
                StringAssert.Contains(exception.Message, "InvalidRequest");
            }
            if (mode == "missing")
            {
                StringAssert.Contains(exception.Message, "odds:20260912:Tokyo:1|Missing");
                StringAssert.Contains(exception.Message, "missing=1");
            }
            if (mode is "invalid-created" or "invalid-held" or "invalid-accepted")
                StringAssert.Contains(exception.Message, "invalidReceipt=2");
            if (mode == "invalid-created-unknown-code")
            {
                StringAssert.Contains(exception.Message, "invalidReceipt=2");
                StringAssert.Contains(exception.Message, "redactedTokens=2");
                StringAssert.Contains(exception.Message, "truncated=true");
                Assert.IsFalse(exception.Message.Contains("unknown-api-key", StringComparison.Ordinal));
            }
        }
    }

    [TestMethod]
    public async Task Discovery_BatchDiagnosticRedactsUnknownResponseTokensAndCapsAsciiOutput()
    {
        var date = new DateOnly(2026, 9, 12);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (target, course) => new JraRaceListPage(
                    "https://example.test/list?token=expected-url-secret", target, course,
                    [new(new(target, course, 1), "race", new(15, 0), null,
                        "https://example.test/card?credential=source-url-secret")]),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        { CoursesByDate = target => target == date ? [RaceCourse.Tokyo] : [] };
        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule,
                    new RecordingBatchSink("unsafe"),
                    timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero)))
                .CollectAsync(CreateDiscoveryTask(date), CancellationToken.None));

        StringAssert.Contains(exception.Message, "race:20260912:Tokyo:1");
        StringAssert.Contains(exception.Message, "Rejected|Redacted");
        StringAssert.Contains(exception.Message, "extra=2");
        StringAssert.Contains(exception.Message, "duplicate=1");
        StringAssert.Contains(exception.Message, "invalidStatus=1");
        StringAssert.Contains(exception.Message, "truncated=true");
        Assert.IsFalse(exception.Message.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(exception.Message.Contains("https://", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(exception.Message.Contains("credential-key", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(exception.Message.All(character => character <= 0x7f));
        Assert.IsLessThanOrEqualTo(2048, System.Text.Encoding.ASCII.GetByteCount(exception.Message));
    }

    [TestMethod]
    public async Task Discovery_CardBoundaryOutOfRange_FallsBackToResultWithoutOddsRequest()
    {
        var today = new DateOnly(2026, 9, 17);
        var date = today.AddDays(-JraNavigator.DefaultRaceCardLookupPeriodDays);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (_, _) => throw new JraNavigationException(
                    "card retired",
                    JraNavigationFailureReason.OutOfDisplayedRange),
                RaceResultListFactory = (target, course) => new JraRaceListPage(
                    "https://www.jra.go.jp/JRADB/accessS.html", target, course,
                    [new(new(target, course, 11), "test", null, null,
                        "/JRADB/accessS.html?CNAME=pw01sde0106123456781120260912/2F")]),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        { CoursesByDate = target => target == date ? [RaceCourse.Nakayama] : [] };
        var sink = new RecordingSink();
        var handler = new JraRaceDiscoveryCollectionHandler(
            sessions,
            _ => schedule,
            sink,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 16, 15, 0, 0, TimeSpan.Zero)));

        var result = await handler.CollectAsync(CreateDiscoveryTask(date), CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, result.Result);
        Assert.HasCount(1, sessions.LastNavigator!.RaceCardListRequests);
        Assert.HasCount(1, sessions.LastNavigator.RaceResultListRequests);
        Assert.HasCount(1, sink.Requests);
        var request = sink.Requests.Single();
        Assert.AreEqual(CollectionResourceType.Race, request.Resource.Type);
        Assert.AreEqual(CollectionLane.Background, request.Lane);
        Assert.AreEqual(10, request.Priority);
        Assert.AreEqual(
            new Uri("https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde0106123456781120260912/2F"),
            request.ExplicitUrl);
        Assert.IsFalse(request.Attributes.ContainsKey("startTime"));
    }

    [TestMethod]
    public async Task Discovery_ResultFallbackReturnsUnsupportedPage_ReturnsUnexpectedPageInsteadOfSucceedingEmpty()
    {
        var today = new DateOnly(2026, 9, 17);
        var date = today.AddDays(-JraNavigator.DefaultRaceCardLookupPeriodDays);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (_, _) => throw new JraNavigationException(
                    "card retired",
                    JraNavigationFailureReason.OutOfDisplayedRange),
                RaceResultListFactory = (_, _) => new JraCalendarPage(
                    "https://www.jra.go.jp/keiba/calendar/",
                    new YearMonth(date.Year, date.Month),
                    []),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        { CoursesByDate = target => target == date ? [RaceCourse.Nakayama] : [] };
        var sink = new RecordingSink();
        var handler = new JraRaceDiscoveryCollectionHandler(
            sessions,
            _ => schedule,
            sink,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 16, 15, 0, 0, TimeSpan.Zero)));

        var result = await handler.CollectAsync(CreateDiscoveryTask(date), CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.UnexpectedPage, result.Result);
        Assert.AreEqual("RaceResultListPageKindMismatch", result.ErrorCode);
        StringAssert.Contains(result.ErrorMessage, "Kind=Calendar");
        Assert.AreEqual(new Uri("https://www.jra.go.jp/keiba/calendar/"), result.FinalUrl);
        Assert.IsEmpty(sink.Requests);
    }

    [TestMethod]
    public async Task Discovery_ResolvesRelativeRaceUrlsAgainstTheSourcePage()
    {
        var date = new DateOnly(2026, 9, 12);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (target, course) => new JraRaceListPage(
                    "https://www.jra.go.jp/JRADB/accessD.html", target, course,
                    [new(new(target, course, 7), "test", new(13, 10),
                        "/JRADB/accessD.html?CNAME=pw01dde1001123456780720260912/25",
                        "/JRADB/accessS.html?CNAME=pw01sde1001123456780720260912/2F")]),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        { CoursesByDate = target => target == date ? [RaceCourse.Sapporo] : [] };
        var sink = new RecordingSink();

        await new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, sink,
                timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero)))
            .CollectAsync(CreateDiscoveryTask(date), CancellationToken.None);

        Assert.AreEqual(new Uri("https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01dde1001123456780720260912/25"),
            sink.Requests.Single(x => x.Resource.Type == CollectionResourceType.Race).ExplicitUrl);
    }

    [TestMethod]
    public async Task Discovery_DoesNotPersistParameterlessSelectionPagesAsDetailLocations()
    {
        var date = new DateOnly(2026, 9, 12);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (target, course) => new JraRaceListPage(
                    "https://www.jra.go.jp/JRADB/accessD.html", target, course,
                    [new(new(target, course, 7), "test", new(13, 10),
                        "/JRADB/accessD.html", "/JRADB/accessS.html")]),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        { CoursesByDate = target => target == date ? [RaceCourse.Sapporo] : [] };
        var sink = new RecordingSink();

        await new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, sink,
                timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero)))
            .CollectAsync(CreateDiscoveryTask(date), CancellationToken.None);

        Assert.IsNull(sink.Requests.Single(x => x.Resource.Type == CollectionResourceType.Race).ExplicitUrl);
    }

    [TestMethod]
    public async Task Discovery_PageBelongsToAnotherCourse_CreatesNoRequests()
    {
        var date = new DateOnly(2026, 9, 13);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (target, _) => new JraRaceListPage(
                    "https://example.test/hanshin", target, RaceCourse.Hanshin,
                    [new(new(target, RaceCourse.Hanshin, 8), "wrong course", new(14, 0),
                        "https://example.test/card/8", null)]),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        { CoursesByDate = target => target == date ? [RaceCourse.Nakayama] : [] };
        var sink = new RecordingSink();
        var handler = new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, sink,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero)));

        await Assert.ThrowsExactlyAsync<JraRaceIdentityMismatchException>(() =>
            handler.CollectAsync(CreateDiscoveryTask(date), CancellationToken.None));

        Assert.IsEmpty(sink.Requests);
    }

    [TestMethod]
    public async Task BackfillDiscovery_VisitsOnlyItsDayAndPropagatesBatchId()
    {
        var date = new DateOnly(2020, 1, 5);
        var visited = new List<DateOnly>();
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceResultListFactory = (target, course) => new JraRaceResultPage(
                    "https://example.test/result/1", new(target, course, 1), "race", []),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        {
            CoursesByDate = target =>
            {
                visited.Add(target);
                return target == date ? [RaceCourse.Tokyo] : [];
            },
        };
        var sink = new RecordingSink();
        var handler = new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, sink);
        var task = new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(CollectionResourceType.Race, "JRA", "backfill:20200105"), new("race-discovery"), 1,
            CollectionReason.Backfill, CollectionLane.Background, 10, "lease", DateTimeOffset.UtcNow.AddMinutes(5),
            date, new Dictionary<string, string> { ["batchId"] = "jra:2020-01" });

        await handler.CollectAsync(task, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { date }, visited);
        Assert.IsTrue(sink.Requests.All(x => x.Attributes.GetValueOrDefault("batchId") == "jra:2020-01"));
        Assert.IsTrue(sink.Requests.All(x => x.Reason == CollectionReason.Backfill));
    }

    [TestMethod]
    public async Task PeriodRecollection_VisitsOnlyItsEffectiveDateAndPropagatesBatchMetadata()
    {
        var date = new DateOnly(2020, 1, 5);
        var visited = new List<DateOnly>();
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceResultListFactory = (target, course) => new JraRaceResultPage(
                    "https://example.test/result/1", new(target, course, 1), "race", []),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        {
            CoursesByDate = target =>
            {
                visited.Add(target);
                return target == date ? [RaceCourse.Tokyo] : [];
            },
        };
        var sink = new RecordingSink();
        var handler = new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, sink);
        var task = new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(CollectionResourceType.Race, "JRA", "period-recollection:20200105"), new("race-discovery"), 1,
            CollectionReason.PeriodRecollection, CollectionLane.Background, 10, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), date,
            new Dictionary<string, string> { ["batchId"] = "period:2020-01-05" });

        await handler.CollectAsync(task, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { date }, visited);
        Assert.HasCount(1, sink.Requests);
        Assert.AreEqual(CollectionResourceType.Race, sink.Requests[0].Resource.Type);
        Assert.AreEqual(new CollectionDefinitionId("race-detail"), sink.Requests[0].Definition);
        Assert.AreEqual(CollectionReason.PeriodRecollection, sink.Requests[0].Reason);
        Assert.AreEqual(date, sink.Requests[0].EffectiveDate);
        Assert.AreEqual("period:2020-01-05", sink.Requests[0].Attributes["batchId"]);
    }

    [TestMethod]
    public async Task FutureUnpublishedRaceList_ReturnsWaitingAndKeepsRequestsAlreadyDiscovered()
    {
        var today = new DateOnly(2026, 9, 12);
        var future = new DateOnly(2026, 9, 19);
        var now = new DateTimeOffset(2026, 9, 12, 1, 0, 0, TimeSpan.Zero); // 10:00 JST
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (target, course) => target == future
                    ? throw new JraNavigationException("not published", JraNavigationFailureReason.NotYetPublished)
                    : new JraRaceListPage("https://example.test/list", target, course,
                        [new(new(target, course, 11), "test", new(15, 30),
                            "https://example.test/card/11", "https://example.test/result/11")]),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        {
            CoursesByDate = target => target == today || target == future ? [RaceCourse.Tokyo] : [],
        };
        var sink = new RecordingSink();
        var handler = new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, sink,
            Options.Create(new RaceDiscoveryCollectionOptions()), new FixedTimeProvider(now));

        var result = await handler.CollectAsync(CreateDiscoveryTask(today), CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.ResourceNotYetAvailable, result.Result);
        Assert.AreEqual("RaceListNotYetAvailable", result.ErrorCode);
        Assert.IsNotNull(result.RetryAt);
        Assert.IsGreaterThan(now, result.RetryAt.Value);
        Assert.HasCount(2, sink.Requests);
        Assert.IsTrue(sink.Requests.All(x => x.Resource.Id.StartsWith("20260912", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task TodayUnexpectedPage_IsNotClassifiedAsWaiting()
    {
        var today = new DateOnly(2026, 9, 12);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (_, _) => new JraUnknownPage("https://example.test/unexpected", "unexpected"),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        { CoursesByDate = target => target == today ? [RaceCourse.Tokyo] : [] };
        var handler = new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, new RecordingSink(),
            Options.Create(new RaceDiscoveryCollectionOptions()),
            new FixedTimeProvider(new(2026, 9, 11, 15, 30, 0, TimeSpan.Zero))); // 9/12 00:30 JST

        await Assert.ThrowsExactlyAsync<JraCollectionException>(() =>
            handler.CollectAsync(CreateDiscoveryTask(today), CancellationToken.None));
    }

    [TestMethod]
    public async Task FutureHttpFailure_IsNotMisclassifiedAsPublicationWaiting()
    {
        var today = new DateOnly(2026, 9, 12);
        var future = today.AddDays(7);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (_, _) => throw new HttpRequestException("unavailable", null,
                    System.Net.HttpStatusCode.ServiceUnavailable),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        { CoursesByDate = target => target == future ? [RaceCourse.Tokyo] : [] };
        var handler = new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, new RecordingSink(),
            Options.Create(new RaceDiscoveryCollectionOptions()),
            new FixedTimeProvider(new(2026, 9, 12, 1, 0, 0, TimeSpan.Zero)));

        var exception = await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            handler.CollectAsync(CreateDiscoveryTask(today), CancellationToken.None));
        Assert.AreEqual(System.Net.HttpStatusCode.ServiceUnavailable, exception.StatusCode);
    }

    [TestMethod]
    public async Task FutureParseFailure_IsNotMisclassifiedAsPublicationWaiting()
    {
        var today = new DateOnly(2026, 9, 12);
        var future = today.AddDays(7);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (_, _) => throw new JraPageParseException(
                    JraPageKind.RaceList, "https://example.test/broken", "broken table"),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        { CoursesByDate = target => target == future ? [RaceCourse.Tokyo] : [] };
        var handler = new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, new RecordingSink(),
            Options.Create(new RaceDiscoveryCollectionOptions()),
            new FixedTimeProvider(new(2026, 9, 12, 1, 0, 0, TimeSpan.Zero)));

        await Assert.ThrowsExactlyAsync<JraPageParseException>(() =>
            handler.CollectAsync(CreateDiscoveryTask(today), CancellationToken.None));
    }

    [TestMethod]
    public async Task UnpublishedDay_DoesNotPreventDiscoveryOfLaterPublishedDay()
    {
        var today = new DateOnly(2026, 9, 12);
        var unpublished = today.AddDays(6);
        var published = today.AddDays(7);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (target, course) => target == unpublished
                    ? throw new JraNavigationException("not published", JraNavigationFailureReason.NotYetPublished)
                    : new JraRaceListPage("https://example.test/list", target, course,
                        [new(new(target, course, 11), "published", new(15, 30),
                            "https://example.test/card/11", "https://example.test/result/11")]),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        {
            CoursesByDate = target => target == unpublished || target == published ? [RaceCourse.Tokyo] : [],
        };
        var sink = new RecordingSink();
        var handler = new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, sink,
            Options.Create(new RaceDiscoveryCollectionOptions()),
            new FixedTimeProvider(new(2026, 9, 12, 1, 0, 0, TimeSpan.Zero)));

        var result = await handler.CollectAsync(CreateDiscoveryTask(today), CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.ResourceNotYetAvailable, result.Result);
        Assert.HasCount(2, sink.Requests);
        Assert.IsTrue(sink.Requests.All(x => x.Resource.Id.StartsWith("20260919", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task TomorrowUnpublished_UsesConfiguredNearPublicationInterval()
    {
        var today = new DateOnly(2026, 9, 12);
        var tomorrow = today.AddDays(1);
        var now = new DateTimeOffset(2026, 9, 12, 1, 0, 0, TimeSpan.Zero);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (_, _) => throw new JraNavigationException(
                    "not published", JraNavigationFailureReason.NotYetPublished),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        { CoursesByDate = target => target == tomorrow ? [RaceCourse.Tokyo] : [] };
        var handler = new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, new RecordingSink(),
            Options.Create(new RaceDiscoveryCollectionOptions { NearPublicationRetryMinutes = 47 }),
            new FixedTimeProvider(now));

        var result = await handler.CollectAsync(CreateDiscoveryTask(today), CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.ResourceNotYetAvailable, result.Result);
        Assert.AreEqual(now.AddMinutes(47), result.RetryAt);
    }

    private static LeasedCollectionTask CreateDiscoveryTask(DateOnly date) => new(Guid.NewGuid(), Guid.NewGuid(),
        new(CollectionResourceType.Race, "JRA", $"discovery:{date:yyyyMMdd}00"), new("race-discovery"), 1,
        CollectionReason.Discovery, CollectionLane.Realtime, 70, "lease", DateTimeOffset.UtcNow.AddMinutes(5),
        date, new Dictionary<string, string>());

    private static CollectionRequestBulkItemDto CreateDiscoveryBatchItem(DateOnly date) => new(
        $"race:{date:yyyyMMdd}:Tokyo:11", CollectionResourceType.Race.ToString(), "JRA",
        $"{date:yyyyMMdd}:Tokyo:11", "race-detail",
        HorseRacingPrediction.Contracts.Collection.CollectionDefinitionRevisions.RaceDetail,
        CollectionReason.Discovery.ToString(), CollectionLane.Realtime.ToString(), 100,
        "https://example.test/card/11", date,
        new Dictionary<string, string>
        {
            ["course"] = "東京",
            ["number"] = "11",
            ["startTime"] = "15:30",
        });

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingSink : ICollectionRequestSink
    {
        public List<(ResourceKey Resource, CollectionDefinitionId Definition, CollectionReason Reason,
            CollectionLane Lane, int Priority, DateOnly EffectiveDate,
            IReadOnlyDictionary<string, string> Attributes, Uri? ExplicitUrl, int RequestedRevision)> Requests
        { get; } = [];
        public Task RequestAsync(ResourceKey resource, CollectionDefinitionId definition, int requestedRevision,
            CollectionReason reason,
            CollectionLane lane, int priority, Uri? explicitUrl, DateOnly effectiveDate,
            IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken)
        {
            Requests.Add((resource, definition, reason, lane, priority, effectiveDate, attributes, explicitUrl, requestedRevision));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingBatchSink(string? failureMode = null) : ICollectionRequestSink
    {
        public List<(CollectionRequestBulkRequest Request, CollectionRequestBulkResponse Response)> Batches { get; } = [];
        public List<string> SingleRequests { get; } = [];

        public Task RequestAsync(ResourceKey resource, CollectionDefinitionId definition, int requestedRevision,
            CollectionReason reason, CollectionLane lane, int priority, Uri? explicitUrl, DateOnly effectiveDate,
            IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken)
        {
            SingleRequests.Add(resource.Id);
            return Task.CompletedTask;
        }

        public Task<CollectionRequestBulkResponse> RequestManyAsync(CollectionRequestBulkRequest request,
            CancellationToken cancellationToken)
        {
            if (failureMode == "unsafe")
            {
                var unsafeOutcomes = new CollectionRequestBulkOutcomeDto[]
                {
                    new(request.Items[0].ItemKey, "Rejected", ErrorCode: "credential-key-secret",
                        Message: "secret response body"),
                    new(request.Items[0].ItemKey, "secret-status-token", ErrorCode: "api-key-secret",
                        Message: "secret status body"),
                    new("race:unexpected-secret-token", "Rejected", ErrorCode: "InvalidRequest",
                        Message: "secret extra body"),
                    new(null!, "Rejected", ErrorCode: "InvalidRequest", Message: "null key secret body"),
                };
                return Task.FromResult(new CollectionRequestBulkResponse(unsafeOutcomes));
            }
            var outcomes = request.Items.Select(item => new CollectionRequestBulkOutcomeDto(
                item.ItemKey,
                failureMode switch
                {
                    "rejected" => "Rejected",
                    "invalid-held" => "Held",
                    "invalid-accepted" => "Accepted",
                    _ => "Created",
                },
                RequestId: failureMode is "invalid-created" or "invalid-created-unknown-code" or "invalid-held"
                    ? null : Guid.NewGuid(),
                TaskId: failureMode switch
                {
                    "invalid-created" or "invalid-created-unknown-code" => null,
                    "invalid-held" => Guid.Empty,
                    "invalid-accepted" => Guid.NewGuid(),
                    _ => Guid.NewGuid(),
                },
                CreatedTask: true,
                ErrorCode: failureMode == "rejected" ? "InvalidRequest"
                    : failureMode == "invalid-created-unknown-code" ? "unknown-api-key" : null,
                Message: failureMode == "rejected" ? "Rejected for regression test." : null)).ToArray();
            if (failureMode == "missing" && outcomes.Length > 0) outcomes = outcomes[..^1];
            var response = new CollectionRequestBulkResponse(outcomes);
            Batches.Add((request, response));
            return Task.FromResult(response);
        }
    }
}
