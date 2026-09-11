using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Collector.Tests.TestSupport;
using HorseRacingPrediction.Contracts;
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
}
