using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Collector.Tests.TestSupport;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class JraSubjectCollectionHandlerTests
{
    [TestMethod]
    public async Task OwnerIdentityApiClient_DistinguishesRegisteredAndMissingOwners()
    {
        using var http = new HttpClient(new OwnerLookupHandler())
        { BaseAddress = new Uri("https://api.test/") };
        var client = new OwnerIdentityApiClient(http);

        Assert.IsTrue(await client.ExistsAsync("owner-known", CancellationToken.None));
        Assert.IsFalse(await client.ExistsAsync("owner-missing", CancellationToken.None));
    }

    [TestMethod]
    public async Task IdentityWithoutName_RemainsSubjectNotIdentified()
    {
        var handler = new JraSubjectProfileCollectionHandler(
            JraSubjectCollectionDefinitions.For(ResourceType.Owner), new FakeJraSessionFactory(),
            new RecordingProfileSink());
        var task = new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Owner, "JRA", "owner-missing"), new("owner-identity"), 1,
            CollectionReason.Recovery, CollectionLane.Normal, 70, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), new DateOnly(2026, 9, 14),
            new Dictionary<string, string>());

        var completion = await handler.CollectAsync(task, CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.ResourceNotFound, completion.Result);
        Assert.AreEqual("SubjectNotIdentified", completion.ErrorCode);
        Assert.AreEqual("SubjectIdentification:MissingName", completion.PageIdentification);
    }

    [TestMethod]
    public async Task ScheduledRefresh_WithPreservedNameAndLocation_CollectsProfile()
    {
        var url = new Uri("https://www.jra.go.jp/JRADB/accessU.html?CNAME=scheduled-horse");
        var sink = new RecordingProfileSink();
        var handler = new JraSubjectProfileCollectionHandler(
            JraSubjectCollectionDefinitions.For(ResourceType.Horse), new FakeJraSessionFactory
            {
                ConfigureNavigator = () => new FakeJraNavigator
                {
                    DirectUrlFactory = _ => new JraSubjectPage(
                        new JraSubjectProfileDto("Horse", "定期更新馬", url.AbsoluteUri, url.AbsoluteUri,
                            new Dictionary<string, string> { ["生年月日"] = "2020年1月1日" },
                            DateTimeOffset.UtcNow), [], null),
                },
            }, sink);
        var task = new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Horse, "JRA", "horse-scheduled"), new("horse-profile"), 4,
            CollectionReason.ScheduledRefresh, CollectionLane.Background, 30, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), new DateOnly(2026, 9, 25),
            new Dictionary<string, string>
            {
                ["name"] = "定期更新馬",
                ["sourceIdentity"] = url.AbsoluteUri,
            }, [new(1, url, ResourceLocationSource.Explicit, ResourceLocationStatus.Active, null)]);

        var completion = await handler.CollectAsync(task, CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, completion.Result);
        Assert.AreEqual("HorseProfile:JRA:horse-scheduled", completion.PageIdentification);
        Assert.HasCount(1, sink.Saves);
    }

    [TestMethod]
    public async Task OwnerIdentity_WithoutLocation_UsesRaceEntryNameWithoutNavigation()
    {
        var handler = new JraSubjectProfileCollectionHandler(
            JraSubjectCollectionDefinitions.For(ResourceType.Owner), new FakeJraSessionFactory
            {
                ConfigureNavigator = () => new FakeJraNavigator(),
            }, new RecordingProfileSink(), ownerIdentities: new StubOwnerIdentityVerifier(true));
        var task = new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Owner, "JRA", "owner-a"), new("owner-identity"), 1,
            CollectionReason.Discovery, CollectionLane.Background, 30, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), new DateOnly(2026, 9, 14),
            new Dictionary<string, string> { ["name"] = "テスト馬主" });

        var completion = await handler.CollectAsync(task, CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, completion.Result);
        Assert.AreEqual("OwnerIdentity:JRA:owner-a", completion.PageIdentification);
        Assert.IsNull(completion.RequestedUrl);
        Assert.IsNull(completion.FinalUrl);
    }

    [TestMethod]
    public async Task OwnerIdentity_WithValidatedLocation_SucceedsWithoutProfileWrite()
    {
        var url = new Uri("https://www.jra.go.jp/JRADB/accessO.html?CNAME=owner-001");
        var sink = new RecordingProfileSink();
        var handler = new JraSubjectProfileCollectionHandler(
            JraSubjectCollectionDefinitions.For(ResourceType.Owner), new FakeJraSessionFactory
            {
                ConfigureNavigator = () => new FakeJraNavigator
                {
                    DirectUrlFactory = _ => new JraSubjectPage(
                        new JraSubjectProfileDto("Owner", "テスト馬主", "owner-001", url.AbsoluteUri,
                            new Dictionary<string, string>(), DateTimeOffset.UtcNow), [], null),
                },
            }, sink, ownerIdentities: new StubOwnerIdentityVerifier(true));
        var task = new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Owner, "JRA", "owner-a"), new("owner-identity"), 1,
            CollectionReason.Recovery, CollectionLane.Normal, 70, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), new DateOnly(2026, 9, 14),
            new Dictionary<string, string> { ["name"] = "テスト馬主" },
            [new(1, url, ResourceLocationSource.Manual, ResourceLocationStatus.Unknown, null)]);

        var completion = await handler.CollectAsync(task, CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, completion.Result);
        Assert.IsEmpty(sink.Saves);
        Assert.IsNull(completion.RequestedUrl);
        Assert.IsNull(completion.FinalUrl);
    }

    [TestMethod]
    public async Task OwnerIdentity_UnknownCanonicalOwner_RemainsSubjectNotIdentified()
    {
        var handler = new JraSubjectProfileCollectionHandler(
            JraSubjectCollectionDefinitions.For(ResourceType.Owner), new FakeJraSessionFactory(),
            new RecordingProfileSink(), ownerIdentities: new StubOwnerIdentityVerifier(false));
        var task = new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Owner, "JRA", "owner-unknown"), new("owner-identity"), 1,
            CollectionReason.Discovery, CollectionLane.Background, 30, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), new DateOnly(2026, 9, 14),
            new Dictionary<string, string> { ["name"] = "未登録馬主" });

        var completion = await handler.CollectAsync(task, CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.ResourceNotFound, completion.Result);
        Assert.AreEqual("SubjectNotIdentified", completion.ErrorCode);
        Assert.AreEqual("SubjectIdentification:OwnerNotRegistered", completion.PageIdentification);
    }

    [TestMethod]
    [DataRow("https://www.jra.go.jp/JRADB/accessR.html")]
    [DataRow("https://www.jra.go.jp/JRADB/accessK.html")]
    [DataRow("https://www.jra.go.jp/JRADB/accessS.html")]
    [DataRow("https://www.jra.go.jp/JRADB/accessD.html")]
    public async Task ParameterlessSelectionLocation_IsIgnoredAndNameDiscoveryIsUsed(string rawUrl)
    {
        var directNavigations = 0;
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                DirectUrlFactory = _ =>
                {
                    directNavigations++;
                    throw new AssertFailedException("選択ページURLへ遷移してはいけません。");
                },
                SubjectFactory = _ => SubjectPage("A", []),
            },
        };
        var task = SubjectTask("horse-a", "A", new Dictionary<string, string>()) with
        {
            Locations = [new(1, new Uri(rawUrl), ResourceLocationSource.Discovered,
                ResourceLocationStatus.Active, null)],
        };

        var completion = await new JraSubjectProfileCollectionHandler(
                JraSubjectCollectionDefinitions.For(ResourceType.Horse), sessions, new RecordingProfileSink())
            .CollectAsync(task, CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, completion.Result);
        Assert.AreEqual(0, directNavigations);
        Assert.AreEqual("NonTerminalJraUrlIgnored", completion.LocationOutcomes!.Single().ErrorCode);
        Assert.AreNotEqual(rawUrl, completion.RequestedUrl?.AbsoluteUri);
    }

    [TestMethod]
    public async Task ProfileLocationFailure_FallsBackByNameWithoutFailedSourceIdentity()
    {
        var failedUrl = new Uri("https://www.jra.go.jp/broken/horse-a");
        JraSubjectIdentity? discoveryIdentity = null;
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                DirectUrlFactory = _ => throw new HttpRequestException("gone"),
                SubjectFactory = identity =>
                {
                    discoveryIdentity = identity;
                    return SubjectPage("A", []);
                },
            },
        };
        var task = SubjectTask("horse-a", "A", new Dictionary<string, string>
        {
            ["birthDate"] = "2020-01-01",
            ["sourceIdentity"] = failedUrl.AbsoluteUri,
        }) with
        {
            Locations = [new(1, failedUrl, ResourceLocationSource.Discovered, ResourceLocationStatus.Active, null)]
        };

        var completion = await new JraSubjectProfileCollectionHandler(
                JraSubjectCollectionDefinitions.For(ResourceType.Horse), sessions, new RecordingProfileSink())
            .CollectAsync(task, CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, completion.Result);
        Assert.IsNotNull(discoveryIdentity);
        Assert.AreEqual("A", discoveryIdentity.Name);
        Assert.AreEqual(new DateOnly(2020, 1, 1), discoveryIdentity.BirthDate);
        Assert.IsNull(discoveryIdentity.SourceIdentity);
        Assert.HasCount(1, completion.LocationOutcomes!);
    }

    [TestMethod]
    public async Task RaceReferencedProfileLocationFailure_RetainsSourceIdentityForDiscovery()
    {
        var failedUrl = new Uri(
            "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002024102539/FC");
        JraSubjectIdentity? discoveryIdentity = null;
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                DirectUrlFactory = _ => throw new HttpRequestException("context-dependent redirect"),
                SubjectFactory = identity =>
                {
                    discoveryIdentity = identity;
                    return SubjectPage("ロンドンコーリング", []);
                },
            },
        };
        var task = SubjectTask("horse-current", "ロンドンコーリング", new Dictionary<string, string>
        {
            ["sourceIdentity"] = failedUrl.AbsoluteUri,
            ["requestedByRaceId"] = "race-current",
            ["discoveredFromType"] = "Race",
            ["discoveredFromProvider"] = "JRA",
            ["discoveredFromId"] = "20260919:Nakayama:2",
            ["referenceRaceDate"] = "2026-09-19",
            ["referenceRaceCourse"] = "Nakayama",
            ["referenceRaceNumber"] = "2",
        }) with
        {
            // Recovery can be requested after the historical race date. The corroborated
            // race metadata, not the task's scheduling date, identifies the evidence race.
            EffectiveDate = new DateOnly(2026, 9, 20),
            Locations = [new(0, failedUrl, ResourceLocationSource.Explicit,
                ResourceLocationStatus.Unknown, null)],
        };

        var completion = await new JraSubjectProfileCollectionHandler(
                JraSubjectCollectionDefinitions.For(ResourceType.Horse), sessions, new RecordingProfileSink())
            .CollectAsync(task, CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, completion.Result);
        Assert.IsNotNull(discoveryIdentity);
        Assert.AreEqual(failedUrl.AbsoluteUri, discoveryIdentity.SourceIdentity);
        Assert.IsNotNull(discoveryIdentity.ReferenceRace);
    }

    [TestMethod]
    public async Task StructuralProfileFailure_IsIsolatedFromPipeline()
    {
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                SubjectFactory = _ => throw new JraCollectionException("調教師情報の見出しを確認できません。"),
            },
        };
        var task = new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Trainer, "JRA", "trainer-a"), new("trainer-profile"), 2,
            CollectionReason.Recovery, CollectionLane.Background, 40, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), null,
            new Dictionary<string, string> { ["name"] = "テスト調教師" });

        var completion = await new JraSubjectProfileCollectionHandler(
                JraSubjectCollectionDefinitions.For(ResourceType.Trainer), sessions, new RecordingProfileSink())
            .CollectAsync(task, CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.PermanentFailure, completion.Result);
        Assert.AreEqual("StructuralPageFailure", completion.ErrorCode);
        Assert.AreEqual(CollectionFailureImpact.Isolated, completion.FailureImpact);
    }

    [TestMethod]
    public async Task RaceCard_DoesNotRecomputeOrRequestSubjectProfileJobsInCollector()
    {
        var date = new DateOnly(2026, 9, 12);
        var race = new RaceId(date, RaceCourse.Tokyo, 11);
        var cardUrl = new Uri("https://example.test/card/11");
        const string horseUrl = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002023106188/45";
        var card = new JraRaceCardPage(cardUrl.AbsoluteUri, race, "test", new(15, 30),
            [new RaceEntry(1, "テスト馬", 1, "テスト騎手", 55, "テスト調教師（美浦）", "テスト馬主",
                HorseSourceIdentity: horseUrl)]);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator { DirectUrlFactory = _ => card },
        };
        var requests = new RecordingRequestSink();
        var raceHandler = new JraRaceCardCollectionHandler(sessions,
            _ => new FakeJraRaceCardCollectionWorkflow(), requests: requests,
            timeProvider: new FixedTimeProvider(new(2026, 9, 11, 0, 0, 0, TimeSpan.Zero)));

        var raceResult = await raceHandler.CollectAsync(new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.RaceCard, "JRA", "20260912:Tokyo:11"), new("race-card"), 1,
            CollectionReason.Discovery, CollectionLane.Realtime, 80, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), date,
            new Dictionary<string, string> { ["course"] = "東京", ["number"] = "11" },
            [new(1, cardUrl, ResourceLocationSource.Discovered, ResourceLocationStatus.Active, null)]),
            CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.ResourceNotYetAvailable, raceResult.Result);
        Assert.IsEmpty(requests.Requests);
        Assert.IsEmpty(requests.BatchRequests);
    }

    [TestMethod]
    public async Task HorseProfile_DeduplicatesParentsAndRejectsSelfReference()
    {
        var descriptor = JraSubjectCollectionDefinitions.For(ResourceType.Horse);
        var currentId = HorseRacingPrediction.ApiClient.DeterministicIdGenerator.BuildHorseId("A");
        var requests = new RecordingRequestSink();
        var sessions = SubjectSessions("A", new Dictionary<string, string>
        {
            ["生年月日"] = "2020年1月1日",
            ["父"] = "A",
            ["母"] = "B",
            ["母馬"] = "B（母の父：C）",
            ["調教師"] = "T",
        });
        var handler = new JraSubjectProfileCollectionHandler(descriptor, sessions,
            new RecordingProfileSink(), requests);

        await handler.CollectAsync(SubjectTask(currentId, "A", new Dictionary<string, string>()), CancellationToken.None);

        Assert.HasCount(2, requests.Requests);
        Assert.HasCount(1, requests.Requests.Where(x => x.Resource.Type == ResourceType.Horse));
        Assert.HasCount(1, requests.Requests.Where(x => x.Resource.Type == ResourceType.Trainer));
        Assert.IsFalse(requests.Requests.Any(x => x.Resource.Id == currentId));
        Assert.IsTrue(requests.Requests.All(x => x.Attributes["discoveryDepth"] == "1"));
    }

    [TestMethod]
    public async Task RealtimeHorseProfile_DemotesPastRaceResultsToNormalLow()
    {
        var descriptor = JraSubjectCollectionDefinitions.For(ResourceType.Horse);
        var historyDate = new DateOnly(2026, 9, 6);
        var historyUrl = new Uri("https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1006202604020520260906/2F");
        var page = SubjectPage("A", [new(historyDate, "中山", "過去走", new(historyUrl.AbsoluteUri, "結果", "content"), null)]);
        var requests = new RecordingRequestSink();
        var handler = new JraSubjectProfileCollectionHandler(descriptor,
            new FakeJraSessionFactory { ConfigureNavigator = () => new FakeJraNavigator { SubjectFactory = _ => page } },
            new RecordingProfileSink(), requests,
            new FixedTimeProvider(new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero)));

        await handler.CollectAsync(SubjectTask("horse-a", "A",
            new Dictionary<string, string> { ["weekendPriorityUntil"] = "2026-09-19" }, CollectionLane.Realtime), CancellationToken.None);

        var request = requests.Requests.Single();
        Assert.AreEqual(ResourceType.Race, request.Resource.Type);
        Assert.AreEqual(new CollectionDefinitionId("race-detail"), request.Definition);
        Assert.AreEqual("20260906:Nakayama:5", request.Resource.Id);
        Assert.AreEqual(CollectionLane.Normal, request.Lane);
        Assert.AreEqual((int)CollectionPriority.Low, request.Priority);
        Assert.AreEqual(historyDate, request.EffectiveDate);
        Assert.AreEqual(historyUrl, request.ExplicitUrl);
    }

    [TestMethod]
    [DataRow(CollectionLane.Normal)]
    [DataRow(CollectionLane.Background)]
    public async Task NonRealtimeHorseProfile_DemotesPastRaceResultsToBackground(CollectionLane sourceLane)
    {
        var descriptor = JraSubjectCollectionDefinitions.For(ResourceType.Horse);
        var historyDate = new DateOnly(2026, 9, 6);
        var historyUrl = new Uri("https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1006202604020520260906/2F");
        var page = SubjectPage("A", [new(historyDate, "中山", "過去走", new(historyUrl.AbsoluteUri, "結果", "content"), null)]);
        var requests = new RecordingRequestSink();
        var handler = new JraSubjectProfileCollectionHandler(descriptor,
            new FakeJraSessionFactory { ConfigureNavigator = () => new FakeJraNavigator { SubjectFactory = _ => page } },
            new RecordingProfileSink(), requests,
            new FixedTimeProvider(new(2026, 9, 20, 0, 0, 0, TimeSpan.Zero)));

        await handler.CollectAsync(SubjectTask("horse-a", "A",
            new Dictionary<string, string> { ["weekendPriorityUntil"] = "2026-09-19" }, sourceLane), CancellationToken.None);

        var request = requests.Requests.Single();
        Assert.AreEqual(CollectionLane.Background, request.Lane);
        Assert.AreEqual((int)CollectionPriority.Background, request.Priority);
    }

    [TestMethod]
    public async Task HorseProfile_DoesNotCreateHorseForPedigreeDescriptionEndingInOffspring()
    {
        var requests = new RecordingRequestSink();
        var sessions = SubjectSessions("A", new Dictionary<string, string>
        {
            ["生年月日"] = "2020年1月1日",
            ["父"] = "パネットーネ 産駒",
            ["母"] = "フロンサック 産駒",
        });
        var handler = new JraSubjectProfileCollectionHandler(
            JraSubjectCollectionDefinitions.For(ResourceType.Horse), sessions,
            new RecordingProfileSink(), requests);

        var completion = await handler.CollectAsync(SubjectTask("horse-a", "A",
            new Dictionary<string, string>()), CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, completion.Result);
        Assert.IsFalse(requests.Requests.Any(x => x.Resource.Type == ResourceType.Horse));
    }

    [TestMethod]
    public async Task JockeyAbsentFromJraDirectoryCompletesAsNotApplicable()
    {
        var navigator = new FakeJraNavigator
        {
            SubjectFactory = identity => throw new JraSubjectIdentificationException(
                JraSubjectIdentificationFailureKind.NoCandidate, identity.SubjectType, identity.Name),
        };
        var handler = new JraSubjectProfileCollectionHandler(
            JraSubjectCollectionDefinitions.For(ResourceType.Jockey),
            new FakeJraSessionFactory { ConfigureNavigator = () => navigator },
            new RecordingProfileSink());
        var task = new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Jockey, "JRA", "jockey-local"), new("jockey-profile"), 1,
            CollectionReason.Discovery, CollectionLane.Normal, 30, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), null,
            new Dictionary<string, string> { ["name"] = "小谷 哲平" });

        var completion = await handler.CollectAsync(task, CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.NotApplicable, completion.Result);
        Assert.AreEqual("SubjectNotInProviderDirectory", completion.ErrorCode);
    }

    [TestMethod]
    public async Task HorseHistory_SeventyUniqueRacesUseOneBatchAndNoSingleRequests()
    {
        var page = SubjectPage("A", Enumerable.Range(0, 70).Select(HistoryRace).ToArray());
        var requests = new RecordingRequestSink();
        var handler = new JraSubjectProfileCollectionHandler(JraSubjectCollectionDefinitions.For(ResourceType.Horse),
            new FakeJraSessionFactory
            {
                ConfigureNavigator = () => new FakeJraNavigator { SubjectFactory = _ => page },
            }, new RecordingProfileSink(), requests,
            new FixedTimeProvider(new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero)));

        var completion = await handler.CollectAsync(SubjectTask("horse-a", "A",
            new Dictionary<string, string> { ["weekendPriorityUntil"] = "2026-09-19" }),
            CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, completion.Result);
        Assert.AreEqual(0, requests.SingleRequestCalls);
        Assert.HasCount(1, requests.BatchRequests);
        Assert.HasCount(70, requests.BatchRequests.Single().Items);
        Assert.HasCount(70, requests.BatchRequests.Single().Items.Select(x => x.ItemKey)
            .Distinct(StringComparer.Ordinal).ToArray());
        Assert.IsTrue(requests.BatchRequests.Single().Items.All(item => item.Lane == "Background"
            && item.Priority == (int)CollectionPriority.Background
            && item.RequestedRevision == HorseRacingPrediction.Contracts.CollectionDefinitionRevisions.RaceDetail
            && item.DefinitionId == "race-detail"
            && item.EffectiveDate.HasValue
            && item.ExplicitUrl is not null
            && item.Attributes!["requestedByHorseId"] == "horse-a"
            && item.Attributes["requestedByHorseName"] == "A"
            && item.Attributes["weekendPriorityUntil"] == "2026-09-19"));
    }

    [TestMethod]
    public async Task HorseHistory_PageOverLimitUsesDeterministicChunks()
    {
        var page = SubjectPage("A", Enumerable.Range(0, 501).Select(HistoryRace).ToArray());
        var requests = new RecordingRequestSink();
        var task = SubjectTask("horse-a", "A", new Dictionary<string, string>());
        var handler = new JraSubjectProfileCollectionHandler(JraSubjectCollectionDefinitions.For(ResourceType.Horse),
            new FakeJraSessionFactory
            {
                ConfigureNavigator = () => new FakeJraNavigator { SubjectFactory = _ => page },
            }, new RecordingProfileSink(), requests);

        await handler.CollectAsync(task, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { 500, 1 }, requests.BatchRequests.Select(x => x.Items.Count).ToArray());
        CollectionAssert.AreEqual(new[]
        {
            $"horse-history:{task.TaskId:N}:p0:c0", $"horse-history:{task.TaskId:N}:p0:c1",
        }, requests.BatchRequests.Select(x => x.BatchId).ToArray());
    }

    [TestMethod]
    public async Task HorseHistory_DeduplicatesCanonicalRaceWithinPage()
    {
        var race = HistoryRace(0);
        var page = SubjectPage("A", [race, race, HistoryRace(1)]);
        var requests = new RecordingRequestSink();
        var handler = new JraSubjectProfileCollectionHandler(JraSubjectCollectionDefinitions.For(ResourceType.Horse),
            new FakeJraSessionFactory
            {
                ConfigureNavigator = () => new FakeJraNavigator { SubjectFactory = _ => page },
            }, new RecordingProfileSink(), requests);

        await handler.CollectAsync(SubjectTask("horse-a", "A", new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.HasCount(1, requests.BatchRequests);
        Assert.HasCount(2, requests.BatchRequests.Single().Items);
    }

    [TestMethod]
    public async Task HorseHistory_NextPageFailureKeepsFirstPageBatch()
    {
        var firstPage = SubjectPage("A", [HistoryRace(0), HistoryRace(1)]);
        var requests = new RecordingRequestSink();
        var handler = new JraSubjectProfileCollectionHandler(JraSubjectCollectionDefinitions.For(ResourceType.Horse),
            new FakeJraSessionFactory
            {
                ConfigureNavigator = () => new FakeJraNavigator
                {
                    SubjectFactory = _ => firstPage,
                    NextHistoryFactory = _ => throw new JraCollectionException("next-page-failure"),
                },
            }, new RecordingProfileSink(), requests);

        await Assert.ThrowsAsync<JraCollectionException>(() => handler.CollectAsync(
            SubjectTask("horse-a", "A", new Dictionary<string, string>()), CancellationToken.None));

        Assert.HasCount(1, requests.BatchRequests);
        Assert.HasCount(2, requests.BatchRequests.Single().Items);
    }

    [TestMethod]
    public async Task HorseHistory_TwoPagesUseSeparatePageBatches()
    {
        var firstPage = SubjectPage("A", [HistoryRace(0)]);
        var secondPage = SubjectPage("A", [HistoryRace(1)]);
        var requests = new RecordingRequestSink();
        var task = SubjectTask("horse-a", "A", new Dictionary<string, string>());
        var handler = new JraSubjectProfileCollectionHandler(JraSubjectCollectionDefinitions.For(ResourceType.Horse),
            new FakeJraSessionFactory
            {
                ConfigureNavigator = () => new FakeJraNavigator
                {
                    SubjectFactory = _ => firstPage,
                    NextHistoryFactory = page => ReferenceEquals(page, firstPage) ? secondPage : null,
                },
            }, new RecordingProfileSink(), requests);

        await handler.CollectAsync(task, CancellationToken.None);

        CollectionAssert.AreEqual(new[]
        {
            $"horse-history:{task.TaskId:N}:p0:c0", $"horse-history:{task.TaskId:N}:p1:c0",
        }, requests.BatchRequests.Select(x => x.BatchId).ToArray());
    }

    [TestMethod]
    public async Task HorseHistory_EmptyMalformedAndExcludedRowsEmitNoBatch()
    {
        var date = new DateOnly(2026, 9, 6);
        var malformed = new HorseHistoryRaceLink(date, "中山", "malformed",
            new("https://www.jra.go.jp/JRADB/accessS.html?CNAME=invalid", "結果", "content"), null);
        var excluded = HistoryRace(0) with { ExclusionReason = "cancelled" };
        var requests = new RecordingRequestSink();
        var handler = new JraSubjectProfileCollectionHandler(JraSubjectCollectionDefinitions.For(ResourceType.Horse),
            new FakeJraSessionFactory
            {
                ConfigureNavigator = () => new FakeJraNavigator
                {
                    SubjectFactory = _ => SubjectPage("A", [malformed, excluded]),
                },
            }, new RecordingProfileSink(), requests);

        await handler.CollectAsync(SubjectTask("horse-a", "A", new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.IsEmpty(requests.BatchRequests);
        Assert.AreEqual(0, requests.SingleRequestCalls);
    }

    [TestMethod]
    public async Task HorseHistory_RepeatedPageFailsAfterSubmittingFirstPageOnce()
    {
        var page = SubjectPage("A", [HistoryRace(0)]);
        var requests = new RecordingRequestSink();
        var handler = new JraSubjectProfileCollectionHandler(JraSubjectCollectionDefinitions.For(ResourceType.Horse),
            new FakeJraSessionFactory
            {
                ConfigureNavigator = () => new FakeJraNavigator
                {
                    SubjectFactory = _ => page,
                    NextHistoryFactory = _ => page,
                },
            }, new RecordingProfileSink(), requests);

        await Assert.ThrowsAsync<JraCollectionException>(() => handler.CollectAsync(
            SubjectTask("horse-a", "A", new Dictionary<string, string>()), CancellationToken.None));

        Assert.HasCount(1, requests.BatchRequests);
    }

    [TestMethod]
    public async Task HorseHistory_HeldRaceReceiptKeepsDiscoverySuccessfulWithoutTaskId()
    {
        var page = SubjectPage("A", [HistoryRace(0)]);
        var requests = new RecordingRequestSink
        {
            BatchResponseFactory = request => new([new(request.Items.Single().ItemKey, "Held", Guid.NewGuid(), null)]),
        };
        var handler = new JraSubjectProfileCollectionHandler(JraSubjectCollectionDefinitions.For(ResourceType.Horse),
            new FakeJraSessionFactory { ConfigureNavigator = () => new FakeJraNavigator { SubjectFactory = _ => page } },
            new RecordingProfileSink(), requests);
        var result = await handler.CollectAsync(SubjectTask("horse-a", "A", new Dictionary<string, string>()), CancellationToken.None);
        Assert.AreEqual(CollectionAttemptResult.Succeeded, result.Result);
        Assert.HasCount(1, requests.BatchRequests);
    }

    [TestMethod]
    public async Task HorseHistory_RejectedOrIdentityLessOutcomeFailsTheAttempt()
    {
        var page = SubjectPage("A", [HistoryRace(0)]);
        var requests = new RecordingRequestSink
        {
            BatchResponseFactory = request => new([
                new(request.Items.Single().ItemKey, "Rejected", ErrorCode: "InvalidRequest"),
            ]),
        };
        var handler = new JraSubjectProfileCollectionHandler(JraSubjectCollectionDefinitions.For(ResourceType.Horse),
            new FakeJraSessionFactory
            {
                ConfigureNavigator = () => new FakeJraNavigator { SubjectFactory = _ => page },
            }, new RecordingProfileSink(), requests);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.CollectAsync(
            SubjectTask("horse-a", "A", new Dictionary<string, string>()), CancellationToken.None));
    }

    [TestMethod]
    public async Task HorseHistory_IncompleteOrInconsistentBatchResponseFailsTheAttempt()
    {
        var page = SubjectPage("A", [HistoryRace(0)]);
        var responseFactories = new Func<CollectionRequestBulkRequest, CollectionRequestBulkResponse>[]
        {
            _ => new([]),
            request => new([
                SuccessfulOutcome(request.Items.Single().ItemKey),
                SuccessfulOutcome(request.Items.Single().ItemKey),
            ]),
            _ => new([SuccessfulOutcome("unknown-race")]),
            request => new([new(request.Items.Single().ItemKey, "Created")]),
            request => new([new(request.Items.Single().ItemKey, "Held")]),
        };

        foreach (var responseFactory in responseFactories)
        {
            var requests = new RecordingRequestSink { BatchResponseFactory = responseFactory };
            var handler = new JraSubjectProfileCollectionHandler(
                JraSubjectCollectionDefinitions.For(ResourceType.Horse),
                new FakeJraSessionFactory
                {
                    ConfigureNavigator = () => new FakeJraNavigator { SubjectFactory = _ => page },
                }, new RecordingProfileSink(), requests);

            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.CollectAsync(
                SubjectTask("horse-a", "A", new Dictionary<string, string>()), CancellationToken.None));
        }

        static CollectionRequestBulkOutcome SuccessfulOutcome(string itemKey) =>
            new(itemKey, "Created", Guid.NewGuid(), Guid.NewGuid());
    }

    [TestMethod]
    public async Task HorseProfile_CyclicParentGraphStopsAtAncestor()
    {
        var descriptor = JraSubjectCollectionDefinitions.For(ResourceType.Horse);
        var aId = HorseRacingPrediction.ApiClient.DeterministicIdGenerator.BuildHorseId("A");
        var firstRequests = new RecordingRequestSink();
        await new JraSubjectProfileCollectionHandler(descriptor,
                SubjectSessions("A", new Dictionary<string, string> { ["生年月日"] = "2020年1月1日", ["父"] = "B" }),
                new RecordingProfileSink(), firstRequests)
            .CollectAsync(SubjectTask(aId, "A", new Dictionary<string, string>()), CancellationToken.None);
        var b = firstRequests.Requests.Single();

        var secondRequests = new RecordingRequestSink();
        await new JraSubjectProfileCollectionHandler(descriptor,
                SubjectSessions("B", new Dictionary<string, string> { ["生年月日"] = "2010年1月1日", ["父"] = "A" }),
                new RecordingProfileSink(), secondRequests)
            .CollectAsync(SubjectTask(b.Resource.Id, "B", b.Attributes), CancellationToken.None);

        Assert.IsEmpty(secondRequests.Requests);
    }

    [TestMethod]
    public async Task HorseProfile_MaximumDepthStopsFurtherExpansion()
    {
        var id = HorseRacingPrediction.ApiClient.DeterministicIdGenerator.BuildHorseId("A");
        var requests = new RecordingRequestSink();
        await new JraSubjectProfileCollectionHandler(JraSubjectCollectionDefinitions.For(ResourceType.Horse),
                SubjectSessions("A", new Dictionary<string, string> { ["生年月日"] = "2020年1月1日", ["父"] = "B" }),
                new RecordingProfileSink(), requests)
            .CollectAsync(SubjectTask(id, "A", new Dictionary<string, string> { ["discoveryDepth"] = "3" }),
                CancellationToken.None);

        Assert.IsEmpty(requests.Requests);
    }

    [TestMethod]
    public async Task ProfileWrite_MissingAuthoritativeSubject_IsIsolatedPermanentFailure()
    {
        var handler = new JraSubjectProfileCollectionHandler(
            JraSubjectCollectionDefinitions.For(ResourceType.Horse),
            SubjectSessions("A", new Dictionary<string, string> { ["生年月日"] = "2020年1月1日" }),
            new NotFoundProfileSink());

        var completion = await handler.CollectAsync(SubjectTask("horse-a", "A", new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.PermanentFailure, completion.Result);
        Assert.AreEqual("SubjectResourceMissing", completion.ErrorCode);
        Assert.AreEqual(CollectionFailureImpact.Isolated, completion.FailureImpact);
        Assert.IsNull(completion.RetryAt);
    }

    [TestMethod]
    public async Task ProfileNavigation_SubjectCannotBeIdentified_IsUnavailable()
    {
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                SubjectFactory = _ => throw new JraCollectionException("同定不能: 公開検索から対象を確認できませんでした。")
            }
        };
        var handler = new JraSubjectProfileCollectionHandler(
            JraSubjectCollectionDefinitions.For(ResourceType.Horse), sessions, new RecordingProfileSink());

        var completion = await handler.CollectAsync(SubjectTask("horse-missing", "missing", new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.ResourceNotFound, completion.Result);
        Assert.AreEqual("SubjectNotIdentified", completion.ErrorCode);
    }

    [TestMethod]
    public async Task ProfileNavigation_StructuredIdentificationFailure_PreservesBoundedEvidenceWithoutRetry()
    {
        var requested = "https://www.jra.go.jp/search?q=missing";
        var final = "https://www.jra.go.jp/profile/observed";
        var candidates = Enumerable.Range(1, 6)
            .Select(index => new JraSubjectIdentificationCandidate($"candidate-{index}",
                $"https://www.jra.go.jp/profile/{index}"))
            .ToArray();
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                SubjectFactory = _ => throw new JraSubjectIdentificationException(
                    JraSubjectIdentificationFailureKind.MultipleCandidates,
                    "Horse", "missing", candidates: candidates, requestedUrl: requested, finalUrl: final),
            },
        };
        var handler = new JraSubjectProfileCollectionHandler(
            JraSubjectCollectionDefinitions.For(ResourceType.Horse), sessions, new RecordingProfileSink());

        var completion = await handler.CollectAsync(
            SubjectTask("horse-missing", "missing", new Dictionary<string, string>()), CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.ResourceNotFound, completion.Result);
        Assert.AreEqual("SubjectNotIdentified", completion.ErrorCode);
        Assert.AreEqual("SubjectIdentification:MultipleCandidates", completion.PageIdentification);
        Assert.AreEqual(requested, completion.RequestedUrl?.AbsoluteUri);
        Assert.AreEqual(final, completion.FinalUrl?.AbsoluteUri);
        Assert.IsNull(completion.RetryAt);
        StringAssert.Contains(completion.ErrorMessage, "期待=Horse:missing");
        StringAssert.Contains(completion.ErrorMessage, "candidate-5");
        Assert.IsFalse(completion.ErrorMessage!.Contains("candidate-6", StringComparison.Ordinal));
    }

    private static FakeJraSessionFactory SubjectSessions(string name, IReadOnlyDictionary<string, string> fields) => new()
    {
        ConfigureNavigator = () => new FakeJraNavigator
        {
            SubjectFactory = identity =>
            {
                var url = $"https://www.jra.go.jp/profile/{name}";
                return new JraSubjectPage(new JraSubjectProfileDto("Horse", name, url, url, fields.ToDictionary(),
                    DateTimeOffset.UtcNow), [], null);
            },
        },
    };

    [TestMethod]
    public async Task HorseProfile_ValidRaceProvenance_ReachesNavigatorAsReferenceRace()
    {
        JraSubjectIdentity? received = null;
        var page = SubjectPage("ロンドンコーリング", []);
        var handler = new JraSubjectProfileCollectionHandler(
            JraSubjectCollectionDefinitions.For(ResourceType.Horse),
            new FakeJraSessionFactory
            {
                ConfigureNavigator = () => new FakeJraNavigator
                {
                    SubjectFactory = identity =>
                    {
                        received = identity;
                        return page;
                    },
                },
            }, new RecordingProfileSink());
        var task = SubjectTask("horse-london", "ロンドンコーリング", new Dictionary<string, string>
        {
            ["requestedByRaceId"] = "domain-race",
            ["discoveredFromType"] = "Race",
            ["discoveredFromProvider"] = "JRA",
            ["discoveredFromId"] = "20260912:Nakayama:6",
            ["referenceRaceDate"] = "2026-09-12",
            ["referenceRaceCourse"] = "Nakayama",
            ["referenceRaceNumber"] = "6",
        });

        var completion = await handler.CollectAsync(task, CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, completion.Result);
        Assert.AreEqual(new RaceId(new DateOnly(2026, 9, 12), RaceCourse.Nakayama, 6), received!.ReferenceRace);
    }

    [TestMethod]
    public async Task HorseProfile_InconsistentRaceProvenance_DoesNotReachNavigatorAsReferenceRace()
    {
        JraSubjectIdentity? received = null;
        var page = SubjectPage("ロンドンコーリング", []);
        var handler = new JraSubjectProfileCollectionHandler(
            JraSubjectCollectionDefinitions.For(ResourceType.Horse),
            new FakeJraSessionFactory
            {
                ConfigureNavigator = () => new FakeJraNavigator
                {
                    SubjectFactory = identity =>
                    {
                        received = identity;
                        return page;
                    },
                },
            }, new RecordingProfileSink());
        var task = SubjectTask("horse-london", "ロンドンコーリング", new Dictionary<string, string>
        {
            ["requestedByRaceId"] = "domain-race",
            ["discoveredFromType"] = "Race",
            ["discoveredFromProvider"] = "JRA",
            ["discoveredFromId"] = "20260912:Tokyo:6",
            ["referenceRaceDate"] = "2026-09-12",
            ["referenceRaceCourse"] = "Nakayama",
            ["referenceRaceNumber"] = "6",
        });

        var completion = await handler.CollectAsync(task, CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, completion.Result);
        Assert.IsNull(received!.ReferenceRace);
    }

    private static JraSubjectPage SubjectPage(string name, IReadOnlyList<HorseHistoryRaceLink> races)
    {
        var url = $"https://www.jra.go.jp/profile/{name}";
        return new(new JraSubjectProfileDto("Horse", name, url, url,
            new Dictionary<string, string> { ["生年月日"] = "2020年1月1日" }, DateTimeOffset.UtcNow), races, null);
    }

    private static LeasedCollectionTask SubjectTask(string id, string name,
        IReadOnlyDictionary<string, string> inherited, CollectionLane lane = CollectionLane.Background)
    {
        var attributes = new Dictionary<string, string>(inherited) { ["name"] = name };
        return new(Guid.NewGuid(), Guid.NewGuid(), new(ResourceType.Horse, "JRA", id),
            new("horse-profile"), 1, CollectionReason.Discovery, lane, 30, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), new DateOnly(2026, 9, 12), attributes);
    }

    private sealed class RecordingRequestSink : ICollectionRequestSink
    {
        public List<Request> Requests { get; } = [];
        public List<CollectionRequestBulkRequest> BatchRequests { get; } = [];
        public int SingleRequestCalls { get; private set; }
        public Func<CollectionRequestBulkRequest, CollectionRequestBulkResponse>? BatchResponseFactory { get; init; }

        public Task<CollectionRequestBulkResponse> RequestManyAsync(CollectionRequestBulkRequest request,
            CancellationToken cancellationToken)
        {
            BatchRequests.Add(request);
            foreach (var item in request.Items)
            {
                Requests.Add(new(new(Enum.Parse<ResourceType>(item.ResourceType), item.Provider, item.ResourceId),
                    new(item.DefinitionId),
                    Enum.Parse<CollectionLane>(item.Lane), item.Priority,
                    item.ExplicitUrl is null ? null : new Uri(item.ExplicitUrl), item.EffectiveDate!.Value,
                    item.Attributes ?? new Dictionary<string, string>()));
            }
            return Task.FromResult(BatchResponseFactory?.Invoke(request)
                ?? new CollectionRequestBulkResponse(request.Items.Select(item =>
                    new CollectionRequestBulkOutcome(item.ItemKey, "Created", Guid.NewGuid(), Guid.NewGuid(), true))
                    .ToArray()));
        }
        public Task RequestAsync(ResourceKey resource, CollectionDefinitionId definition, int requestedRevision,
            CollectionReason reason,
            CollectionLane lane, int priority, Uri? explicitUrl, DateOnly effectiveDate,
            IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken)
        {
            SingleRequestCalls++;
            Requests.Add(new(resource, definition, lane, priority, explicitUrl, effectiveDate, attributes));
            return Task.CompletedTask;
        }
    }

    private static HorseHistoryRaceLink HistoryRace(int daysAgo)
    {
        var date = new DateOnly(2026, 9, 6).AddDays(-daysAgo);
        var dateText = date.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        var url = $"https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde10062026040205{dateText}/2F";
        return new(date, "中山", $"過去走{daysAgo}", new(url, "結果", "content"), null);
    }

    private sealed class StubOwnerIdentityVerifier(bool exists) : IOwnerIdentityVerifier
    {
        public Task<bool> ExistsAsync(string ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(exists);
    }

    private sealed class OwnerLookupHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(
            request.RequestUri!.AbsolutePath.EndsWith("owner-known", StringComparison.Ordinal)
                ? System.Net.HttpStatusCode.OK
                : System.Net.HttpStatusCode.NotFound));
    }

    private sealed record Request(ResourceKey Resource, CollectionDefinitionId Definition,
        CollectionLane Lane, int Priority, Uri? ExplicitUrl, DateOnly EffectiveDate,
        IReadOnlyDictionary<string, string> Attributes);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingProfileSink : IJraSubjectProfileSink
    {
        public List<Save> Saves { get; } = [];
        public Task SaveAsync(string subjectType, string subjectId, JraSubjectProfileDto profile,
            CancellationToken cancellationToken)
        {
            Saves.Add(new(subjectType, subjectId, profile));
            return Task.CompletedTask;
        }
    }

    private sealed record Save(string SubjectType, string SubjectId, JraSubjectProfileDto Profile);

    private sealed class NotFoundProfileSink : IJraSubjectProfileSink
    {
        public Task SaveAsync(string subjectType, string subjectId, JraSubjectProfileDto profile,
            CancellationToken cancellationToken) => throw new HttpRequestException("not ready", null,
            System.Net.HttpStatusCode.NotFound);
    }
}
