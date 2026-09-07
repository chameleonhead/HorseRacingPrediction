using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Parsing;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed partial class CollectionExecutionService
{
    private static readonly string[] SubjectJobTypes = [AgentJobType.SubjectProfileRefresh, AgentJobType.HorseHistoryDiscovery, AgentJobType.HorseHistoryRace];
    private async Task ExecuteSubjectJobsAsync(string type,DateTimeOffset now,CancellationToken token)
    {
        var jobs=await _stateStore.AcquireReadyJobsAsync(type,now,TimeSpan.Zero,_options.CollectionBatchSize,
            TimeSpan.FromMinutes(Math.Max(1,_options.CollectionLeaseMinutes)),token);
        foreach(var job in jobs)
        {
            using var timeout=CreateJobTimeoutCts(token);
            try
            {
                if(await ExecuteSubjectTaskAsync(type,job.JobId,job.DeduplicationKey,job.Payload,timeout.Token))
                    await _stateStore.CompleteJobAsync(type,job.DeduplicationKey,token);
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested)
            {
                await _stateStore.RequeueJobAsync(type,job.DeduplicationKey,now,"処理が中断されました。",CancellationToken.None);
                throw;
            }
            catch(Exception ex)
            {
                await _stateStore.FailJobAsync(type,job.DeduplicationKey,ex.Message,CancellationToken.None);
                await PausePipelineIfFatalErrorAsync(ex,job.DeduplicationKey);
            }
        }
    }

    private async Task<bool> ExecuteSubjectTaskAsync(string type,string jobId,string key,string payload,CancellationToken token)
    {
        using var api=_httpClientFactory.CreateClient("ProcessingState");
        if(type==AgentJobType.HorseHistoryRace)
        {
            var race=AgentJobPayloadSerializer.Deserialize<HorseHistoryRacePayload>(payload);
            if(race.LinkUrl is null || race.RaceDate is null) throw new InvalidOperationException("出走履歴の識別情報が不足しています。");
            await using var session=await _sessionFactory.CreateAsync(token);
            var page=await session.Navigate.ToHorseHistoryResultAsync(Identity(race.Horse),
                new(race.RaceDate,race.Course,race.RaceName,new PageLinkSnapshot(race.LinkUrl,race.LinkTitle ?? race.RaceName),null),token);
            using var response=await api.PostAsJsonAsync("/api/admin/collection/horse-history/race",
                new PrepareHorseHistoryRaceRequest(page.RaceId.Date,RaceCourseNames.GetJraName(page.RaceId.Course),page.RaceId.Number,page.RaceName!),token);
            response.EnsureSuccessStatusCode();
            var resolved=await response.Content.ReadFromJsonAsync<ResolvedHistoryRace>(token) ?? throw new InvalidOperationException("レースIDを解決できません。");
            var result=await _raceResultWorkflowFactory(session).RefreshPageAsync(page,resolved.RaceId,race.Horse.SubjectId,token);
            if(result.Errors.Count>0) throw new InvalidOperationException(string.Join("; ",result.Errors));
            return true;
        }

        var subject=AgentJobPayloadSerializer.Deserialize<SubjectCollectionPayload>(payload);
        var checkpoint="horse-history-discovered";
        if(type==AgentJobType.SubjectProfileRefresh || !await _stateStore.HasMarkerAsync(checkpoint,jobId,token))
        {
            await using var session=await _sessionFactory.CreateAsync(token);
            var page=await session.Navigate.ToSubjectProfileAsync(Identity(subject),token);
            SubjectProfilePageParser.Validate(page,Identity(subject));
            using var saved=await api.PostAsJsonAsync($"/api/admin/subjects/{subject.SubjectType}/{Uri.EscapeDataString(subject.SubjectId)}/profile",page.Profile,token);
            saved.EnsureSuccessStatusCode();
            if(type==AgentJobType.SubjectProfileRefresh) return true;
            subject=subject with { SourceIdentity=page.Profile.SourceIdentity,
                BirthDate=SubjectProfilePageParser.TryDate(page.Profile.Fields.GetValueOrDefault("生年月日") ?? "",out var birth)?birth:subject.BirthDate };
            var pages=new HashSet<string>();
            while(page is not null)
            {
                if(!pages.Add(string.Join("|",page.Races.Select(r=>r.Key)))) throw new InvalidOperationException("出走履歴のページ送りが進みません。");
                foreach(var race in page.Races)
                {
                    token.ThrowIfCancellationRequested();
                    var childKey=jobId+":"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(race.Key)));
                    var childType=race.ExclusionReason is null ? AgentJobType.HorseHistoryRace : AgentJobType.HorseHistoryExcluded;
                    var child=new HorseHistoryRacePayload(subject,race.Date,race.Course,race.RaceName,race.Link?.Url,race.Link?.Title,race.ExclusionReason);
                    await _stateStore.ScheduleJobAsync(childType,childKey,AgentJobPayloadSerializer.Serialize(child),DateTimeOffset.UtcNow,
                        priority:90,parentJobId:jobId,cancellationToken:token);
                    if(race.ExclusionReason is not null) await _stateStore.CompleteJobAsync(childType,childKey,token);
                }
                page=await session.Navigate.NextHorseHistoryPageAsync(page,token);
            }
            await _stateStore.MarkMarkerAsync(checkpoint,jobId,token);
        }
        // 明示的再試行・中断再開では成功済み子ジョブを維持し、失敗分だけ再投入する。
        var parent=await _stateStore.GetJobDetailAsync(jobId,token) ?? throw new InvalidOperationException("親ジョブが見つかりません。");
        foreach(var failed in parent.ChildJobs.Where(x=>x.Status is AgentJobStatus.Failed or AgentJobStatus.DeadLetter))
            await _stateStore.ForceRequeueJobAsync(failed.JobType,failed.DeduplicationKey,DateTimeOffset.UtcNow,token);
        if(parent.ChildJobs.Count==0) return true;
        await _stateStore.WaitForDependenciesAsync(type,key,token);
        // 子が先に完了した場合も親を待機状態に取り残さない。
        parent=await _stateStore.GetJobDetailAsync(jobId,token) ?? parent;
        if(parent.ChildJobs.All(x=>x.Status==AgentJobStatus.Succeeded)) await _stateStore.CompleteJobAsync(type,key,token);
        else if(parent.ChildJobs.All(x=>x.Status is AgentJobStatus.Succeeded or AgentJobStatus.Failed or AgentJobStatus.DeadLetter))
            await _stateStore.FailJobAsync(type,key,"一部の過去レース取得に失敗しました。",token);
        return false;
    }
    private static JraSubjectIdentity Identity(SubjectCollectionPayload subject)=>new(subject.SubjectType,subject.Name,subject.BirthDate,subject.SourceIdentity);
    private sealed record ResolvedHistoryRace(string RaceId);
}
