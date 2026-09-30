using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Collector.Http;
using HorseRacingPrediction.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class IdentityResolutionIsolationTests
{
    [TestMethod]
    public async Task OfficialParentWithNameOnlyReference_ThroughApiWorkerAndStore_DoesNotStopFollowingTasks()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        const string source = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002014110060/B1";
        var parentId = "horse-" + Guid.NewGuid();
        (await http.PostAsJsonAsync("/api/horses", new RegisterHorseRequest("マテラスカイ", "マテラスカイ", "M", new(2014, 3, 18), parentId))).EnsureSuccessStatusCode();
        var profile = new JraSubjectProfileDto("Horse", "マテラスカイ", source, source,
            new() { ["生年月日"] = "2014年3月18日" }, DateTimeOffset.UtcNow);
        await new JraSubjectProfileApiClient(http).SaveAsync("Horse", parentId, profile, CancellationToken.None);
        var store = app.Services.GetRequiredService<CollectionPlatformStore>();
        var definition = new CollectionDefinitionId("horse-profile");
        await store.RegisterDefinitionAsync(definition, "Horse", CollectionResourceType.Horse, 1, "test", false);
        var resources = Enumerable.Range(0, 3).Select(i => new ResourceKey(CollectionResourceType.Horse, "JRA", "horse-child-" + i)).ToArray();
        var ids = new List<Guid>();
        foreach (var resource in resources)
        {
            var receipt = await store.RequestAsync(resource, definition, 1, CollectionReason.Initial, DateTimeOffset.UtcNow.AddMinutes(-1),
                priority: resource == resources[0] ? 100 : 50, effectiveDate: new(2026, 9, 27));
            ids.Add(receipt.TaskId!.Value);
        }
        var handler = new ParentReferenceHandler(new HttpDataCollectionWriteService(http, new()), resources[0].Id);
        var worker = new CollectionPlatformWorkerClient(http, new([handler]));
        var queue = new WakeQueue();
        var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue,
            Options.Create(new CollectionQueueOptions
            {
                Enabled = true,
                AggregationDelayMilliseconds = 0,
                EnvelopeMaxTasks = 2,
                DefinitionMaxTasks = new() { ["horse-profile"] = 2 }
            }),
            NullLogger<CollectionPlatformOutboxDispatcher>.Instance);
        for (var batch = 0; batch < 2; batch++)
        {
            await dispatcher.DispatchOnceAsync(CancellationToken.None);
            Assert.HasCount(batch + 1, queue.Wakes);
            var json = JsonSerializer.Serialize(new
            {
                Records = new[] { new { messageId = "batch-" + batch,
                body = JsonSerializer.Serialize(queue.Wakes[batch], new JsonSerializerOptions(JsonSerializerDefaults.Web)) } }
            });
            var result = await CollectionLambdaInvocation.ExecuteWakeAsync(json, worker);
            Assert.IsEmpty(result.BatchItemFailures);
        }
        var detail = await store.GetResourceDetailAsync(resources[0], definition);
        Assert.AreEqual(CollectionTaskStatus.Failed, detail!.LatestTask!.Status);
        var attempt = detail.Attempts.Single();
        Assert.AreEqual("HorseIdentityEvidenceRequired", attempt.ErrorCode);
        Assert.AreEqual(422, attempt.HttpStatusCode);
        Assert.AreEqual(2, attempt.BatchTaskCount);
        Assert.AreEqual(1, attempt.BatchTaskOrdinal);
        StringAssert.Contains(attempt.ErrorMessage!, "SubjectName=マテラスカイ");
        Assert.IsFalse((await store.GetPipelineStateAsync()).IsPaused);
        Assert.HasCount(1, await store.GetActionableFailureNotificationsAsync(DateTimeOffset.UtcNow.AddHours(1), 10));
        foreach (var resource in resources.Skip(1))
            Assert.AreEqual(CollectionTaskStatus.Succeeded, (await store.GetResourceDetailAsync(resource, definition))!.LatestTask!.Status);
        Assert.IsNull(await store.AcquireAsync(ids[0], 1, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5)));
        var resolved = await http.PostAsJsonAsync("/api/identity/horse", new ResolveHorseIdentityRequest("マテラスカイ", source));
        resolved.EnsureSuccessStatusCode();
        Assert.AreEqual(parentId, (await resolved.Content.ReadFromJsonAsync<ResolvedIdentityDto>())!.Id);
    }

    private sealed class WakeQueue : ICollectionPlatformTaskQueue
    {
        public List<CollectionWakeSignal> Wakes { get; } = [];
        public Task<CollectionQueueSendReceipt> SendAsync(CollectionDispatchEnvelope envelope, CancellationToken token) =>
            throw new AssertFailedException("Expected wake dispatch.");
        public Task<CollectionQueueSendReceipt> SendWakeAsync(CollectionWakeSignal wake, CancellationToken token)
        {
            Wakes.Add(wake);
            return Task.FromResult(new CollectionQueueSendReceipt("wake-" + Wakes.Count));
        }
    }

    private sealed class ParentReferenceHandler(HttpDataCollectionWriteService writer, string failingResource) : ICollectionDefinitionHandler
    {
        public CollectionDefinitionId DefinitionId => new("horse-profile");
        public CollectionResourceType ResourceType => CollectionResourceType.Horse;
        public async Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task, CancellationToken token)
        {
            if (task.Resource.Id == failingResource)
                await writer.UpsertHorseAsync("マテラスカイ", null, null, null, cancellationToken: token);
            return new(CollectionAttemptResult.Succeeded);
        }
    }
}
