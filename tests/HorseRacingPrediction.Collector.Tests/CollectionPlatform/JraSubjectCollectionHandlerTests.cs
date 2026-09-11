using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Collector.Tests.TestSupport;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class JraSubjectCollectionHandlerTests
{
    [TestMethod]
    public async Task RaceCard_DiscoversHorseJockeyTrainer_AndProfilesAreWrittenByTheirHandlers()
    {
        var date = new DateOnly(2026, 9, 12);
        var race = new RaceId(date, RaceCourse.Tokyo, 11);
        var cardUrl = new Uri("https://example.test/card/11");
        var card = new JraRaceCardPage(cardUrl.AbsoluteUri, race, "test", new(15, 30),
            [new RaceEntry(1, "テスト馬", 1, "テスト騎手", 55, "テスト調教師")]);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator { DirectUrlFactory = _ => card },
        };
        var requests = new RecordingRequestSink();
        var raceHandler = new JraRaceCardCollectionHandler(sessions,
            _ => new FakeJraRaceCardCollectionWorkflow(), requests: requests);

        var raceResult = await raceHandler.CollectAsync(new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.RaceCard, "JRA", "20260912:Tokyo:11"), new("race-card"), 1,
            CollectionReason.Discovery, CollectionLane.Realtime, 80, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), date,
            new Dictionary<string, string> { ["course"] = "東京", ["number"] = "11" },
            [new(1, cardUrl, ResourceLocationSource.Discovered, ResourceLocationStatus.Active, null)]),
            CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, raceResult.Result);
        CollectionAssert.AreEquivalent(new[] { ResourceType.Horse, ResourceType.Jockey, ResourceType.Trainer },
            requests.Requests.Select(x => x.Resource.Type).ToArray());
        var profileSink = new RecordingProfileSink();
        foreach (var request in requests.Requests)
        {
            var descriptor = JraSubjectCollectionDefinitions.For(request.Resource.Type);
            var profileSessions = new FakeJraSessionFactory
            {
                ConfigureNavigator = () => new FakeJraNavigator
                {
                    SubjectFactory = identity =>
                    {
                        var sourceUrl = $"https://www.jra.go.jp/profile/{descriptor.IdPrefix}";
                        var sourceIdentity = identity.SubjectType == "Horse"
                            ? sourceUrl : $"{identity.SubjectType}:{identity.Name}:1990-01-01";
                        return new JraSubjectPage(new JraSubjectProfileDto(identity.SubjectType, identity.Name,
                            sourceIdentity, sourceUrl,
                            new Dictionary<string, string> { ["生年月日"] = "1990年1月1日" },
                            DateTimeOffset.UtcNow), [], null);
                    },
                },
            };
            var handler = new JraSubjectProfileCollectionHandler(descriptor, profileSessions, profileSink);
            var completion = await handler.CollectAsync(new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
                request.Resource, request.Definition, 1, CollectionReason.Discovery, CollectionLane.Normal, 30,
                "lease", DateTimeOffset.UtcNow.AddMinutes(5), date, request.Attributes), CancellationToken.None);
            Assert.AreEqual(CollectionAttemptResult.Succeeded, completion.Result);
        }

        CollectionAssert.AreEquivalent(new[] { "Horse", "Jockey", "Trainer" },
            profileSink.Saves.Select(x => x.SubjectType).ToArray());
        CollectionAssert.AreEquivalent(requests.Requests.Select(x => x.Resource.Id).ToArray(),
            profileSink.Saves.Select(x => x.SubjectId).ToArray());
    }

    [TestMethod]
    public async Task HorseProfile_DeduplicatesParentsAndRejectsSelfReference()
    {
        var descriptor = JraSubjectCollectionDefinitions.For(ResourceType.Horse);
        var currentId = HorseRacingPrediction.ApiClient.DeterministicIdGenerator.BuildEntityId("horse", "A");
        var requests = new RecordingRequestSink();
        var sessions = SubjectSessions("A", new Dictionary<string, string>
        {
            ["生年月日"] = "2020年1月1日", ["父"] = "A", ["母"] = "B", ["母馬"] = "B（母の父：C）",
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
    public async Task HorseProfile_CyclicParentGraphStopsAtAncestor()
    {
        var descriptor = JraSubjectCollectionDefinitions.For(ResourceType.Horse);
        var aId = HorseRacingPrediction.ApiClient.DeterministicIdGenerator.BuildEntityId("horse", "A");
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
        var id = HorseRacingPrediction.ApiClient.DeterministicIdGenerator.BuildEntityId("horse", "A");
        var requests = new RecordingRequestSink();
        await new JraSubjectProfileCollectionHandler(JraSubjectCollectionDefinitions.For(ResourceType.Horse),
                SubjectSessions("A", new Dictionary<string, string> { ["生年月日"] = "2020年1月1日", ["父"] = "B" }),
                new RecordingProfileSink(), requests)
            .CollectAsync(SubjectTask(id, "A", new Dictionary<string, string> { ["discoveryDepth"] = "3" }),
                CancellationToken.None);

        Assert.IsEmpty(requests.Requests);
    }

    [TestMethod]
    public async Task ProfileWrite_SubjectProjectionNotReady_IsRetried()
    {
        var handler = new JraSubjectProfileCollectionHandler(
            JraSubjectCollectionDefinitions.For(ResourceType.Horse),
            SubjectSessions("A", new Dictionary<string, string> { ["生年月日"] = "2020年1月1日" }),
            new NotFoundProfileSink());

        var completion = await handler.CollectAsync(SubjectTask("horse-a", "A", new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.ResourceNotYetAvailable, completion.Result);
        Assert.AreEqual("SubjectProjectionNotReady", completion.ErrorCode);
        Assert.IsNotNull(completion.RetryAt);
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

    private static LeasedCollectionTask SubjectTask(string id, string name,
        IReadOnlyDictionary<string, string> inherited)
    {
        var attributes = new Dictionary<string, string>(inherited) { ["name"] = name };
        return new(Guid.NewGuid(), Guid.NewGuid(), new(ResourceType.Horse, "JRA", id),
            new("horse-profile"), 1, CollectionReason.Discovery, CollectionLane.Background, 30, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), new DateOnly(2026, 9, 12), attributes);
    }

    private sealed class RecordingRequestSink : ICollectionRequestSink
    {
        public List<Request> Requests { get; } = [];
        public Task RequestAsync(ResourceKey resource, CollectionDefinitionId definition, CollectionReason reason,
            CollectionLane lane, int priority, Uri? explicitUrl, DateOnly effectiveDate,
            IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken)
        {
            Requests.Add(new(resource, definition, attributes));
            return Task.CompletedTask;
        }
    }

    private sealed record Request(ResourceKey Resource, CollectionDefinitionId Definition,
        IReadOnlyDictionary<string, string> Attributes);

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
