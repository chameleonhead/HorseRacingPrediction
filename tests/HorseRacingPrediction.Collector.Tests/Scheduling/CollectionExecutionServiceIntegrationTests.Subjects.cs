using System.Net;
using System.Net.Http.Json;
using HorseRacingPrediction.Collector.Scheduling;
using HorseRacingPrediction.Collector.Tests.TestSupport;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Workflow;

namespace HorseRacingPrediction.Collector.Tests.Scheduling;

public sealed partial class CollectionExecutionServiceIntegrationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task HorseHistory_PagesChildrenPartialFailureAndRetryArePersisted(bool single)
    {
        var store=CreateStore();var date=new DateOnly(2026,9,6);
        var subject=new SubjectCollectionPayload("horse-origin","Horse","テスト馬",new(2024,4,11));
        var first=new HorseHistoryRaceLink(date,"中山","レースA",new PageLinkSnapshot("https://www.jra.go.jp/A","レースA"),null);
        var second=new HorseHistoryRaceLink(date,"中山","レースB",new PageLinkSnapshot("https://www.jra.go.jp/B","レースB"),null);
        var excluded=new HorseHistoryRaceLink(date,"海外","海外レース",null,"JRA結果ページなし");
        var profile=new HorseRacingPrediction.Contracts.JraSubjectProfileDto("Horse","テスト馬","horse-public","https://www.jra.go.jp/horse",
            new(){["生年月日"]="2024年4月11日"},DateTimeOffset.UtcNow);
        var page1=new JraSubjectPage(profile,[first],new("https://www.jra.go.jp/next","次へ"));
        var page2=new JraSubjectPage(profile,[first,second,excluded],null);
        var discoveries=0;var attempts=new List<string>();var fail=true;
        var sessions=new FakeJraSessionFactory {ConfigureNavigator=()=>new FakeJraNavigator {
            SubjectFactory=_=>{discoveries++;return page1;},NextHistoryFactory=p=>p==page1?page2:null,
            HistoryResultFactory=(horse,race)=> {attempts.Add(race.RaceName);if(fail && race.RaceName=="レースA") throw new InvalidOperationException("取得失敗");
                Assert.AreEqual("horse-public",horse.SourceIdentity);return new("https://www.jra.go.jp/result",new(date,RaceCourse.Nakayama,race.RaceName=="レースA"?6:7),race.RaceName,[]);}
        }};
        var results=new FakeJraRaceResultCollectionWorkflow();
        var service=CreateService(store,new FakeJraScheduleCollectionWorkflow(),new FakeJraRaceCardCollectionWorkflow(),results,sessions,new SubjectApiFactory());
        var parentId=await store.RequestSubjectCollectionAsync(AgentJobType.HorseHistoryDiscovery,subject,"test",DateTimeOffset.UtcNow);
        async Task Run(string type)
        {
            if(!single) {await service.RunTaskAsync(type,CancellationToken.None);return;}
            var ready=await store.GetPendingCollectionTaskDispatchesAsync(DateTimeOffset.UtcNow,100);
            foreach(var job in ready.Where(x=>x.Notification.JobType==type)) await service.RunSingleTaskAsync(job.Notification,CancellationToken.None);
        }
        await Run(AgentJobType.HorseHistoryDiscovery);
        Assert.AreEqual(3,(await store.GetJobDetailAsync(parentId))!.ChildJobs.Count);
        await Run(AgentJobType.HorseHistoryRace);
        var failed=(await store.GetSubjectCollectionAsync(AgentJobType.HorseHistoryDiscovery,subject.SubjectId))!;
        Assert.AreEqual(AgentJobStatus.Failed,failed.Job.Status);Assert.AreEqual(2,failed.Discovered);
        Assert.AreEqual(1,failed.Completed);Assert.AreEqual(1,failed.Failed);Assert.AreEqual(1,failed.Excluded);
        fail=false;
        await store.RerunJobAsync(parentId,failed.Job.UpdatedAt,"test","失敗分再試行",DateTimeOffset.UtcNow);
        await Run(AgentJobType.HorseHistoryDiscovery);await Run(AgentJobType.HorseHistoryRace);
        Assert.AreEqual(AgentJobStatus.Succeeded,(await store.GetJobDetailAsync(parentId))!.Status);
        Assert.AreEqual(1,discoveries);Assert.AreEqual(1,attempts.Count(x=>x=="レースB"));Assert.AreEqual(2,attempts.Count(x=>x=="レースA"));
    }

    [TestMethod]
    public async Task HorseHistory_ResumesInterruptedDiscoveryWithoutReplacingCompletedChild()
    {
        var store = CreateStore();
        using var cancellation = new CancellationTokenSource();
        var profile = new HorseRacingPrediction.Contracts.JraSubjectProfileDto("Horse", "対象馬", "public-key",
            "https://www.jra.go.jp/horse", new(), DateTimeOffset.UtcNow);
        var first = new HorseHistoryRaceLink(new(2026, 9, 6), "中山", "レースA", new("https://www.jra.go.jp/A", "レースA"), null);
        var second = new HorseHistoryRaceLink(new(2026, 9, 6), "中山", "レースB", new("https://www.jra.go.jp/B", "レースB"), null);
        var page1 = new JraSubjectPage(profile, [first], new("https://www.jra.go.jp/next", "次へ"));
        var page2 = new JraSubjectPage(profile, [second], null);
        var interrupt = true;
        var sessions = new FakeJraSessionFactory { ConfigureNavigator = () => new FakeJraNavigator {
            SubjectFactory = _ => page1,
            NextHistoryFactory = page => {
                if (page != page1) return null;
                if (interrupt) { interrupt = false; cancellation.Cancel(); }
                return page2;
            }
        }};
        var service = CreateService(store, new FakeJraScheduleCollectionWorkflow(), new FakeJraRaceCardCollectionWorkflow(),
            new FakeJraRaceResultCollectionWorkflow(), sessions, new SubjectApiFactory());
        var parentId = await store.RequestSubjectCollectionAsync(AgentJobType.HorseHistoryDiscovery,
            new("horse-origin", "Horse", "対象馬"), "test", DateTimeOffset.UtcNow);
        try { await service.RunTaskAsync(AgentJobType.HorseHistoryDiscovery, cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        var interrupted = (await store.GetJobDetailAsync(parentId))!;
        Assert.AreEqual(AgentJobStatus.Ready, interrupted.Status);
        var completed = interrupted.ChildJobs.Single();
        await store.CompleteJobAsync(completed.JobType, completed.DeduplicationKey);
        await service.RunTaskAsync(AgentJobType.HorseHistoryDiscovery, CancellationToken.None);
        var resumed = (await store.GetJobDetailAsync(parentId))!;
        Assert.AreEqual(2, resumed.ChildJobs.Count);
        Assert.AreEqual(AgentJobStatus.Succeeded, resumed.ChildJobs.Single(x => x.JobId == completed.JobId).Status);
        Assert.AreEqual(AgentJobStatus.WaitingDependency, resumed.Status);
    }

    [TestMethod]
    public async Task ProfileRefresh_RejectsIdentityMismatchBeforeApiWrite()
    {
        var store=CreateStore();var api=new SubjectApiFactory();
        var sessions=new FakeJraSessionFactory {ConfigureNavigator=()=>new FakeJraNavigator {SubjectFactory=_=>new(
            new("Horse","別の馬","key","https://www.jra.go.jp/horse",new(){["生年月日"]="2024年4月11日"},DateTimeOffset.UtcNow),[],null)}};
        var service=CreateService(store,new FakeJraScheduleCollectionWorkflow(),new FakeJraRaceCardCollectionWorkflow(),new FakeJraRaceResultCollectionWorkflow(),sessions,api);
        var id=await store.RequestSubjectCollectionAsync(AgentJobType.SubjectProfileRefresh,new("horse-1","Horse","対象馬"),"test",DateTimeOffset.UtcNow);
        await service.RunTaskAsync(AgentJobType.SubjectProfileRefresh,CancellationToken.None);
        Assert.AreEqual(AgentJobStatus.Failed,(await store.GetJobDetailAsync(id))!.Status);Assert.AreEqual(0,api.Handler.Requests);
    }

    private sealed class SubjectApiFactory : IHttpClientFactory
    {
        public SubjectApiHandler Handler {get;}=new();
        public HttpClient CreateClient(string name)=>new(Handler,false){BaseAddress=new Uri("https://api.test")};
    }
    private sealed class SubjectApiHandler : HttpMessageHandler
    {
        public int Requests {get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {Requests++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new{raceId="race-target"})});}
    }
}
