using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class SubjectIdentificationAutoRecoveryTests
{
    [TestMethod]
    public async Task NewRevisionRecoversOnlyDeterministicFailureAndIsIdempotent()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        var store = application.Services.GetRequiredService<CollectionPlatformStore>();
        var definition = new CollectionDefinitionId("horse-profile");
        await store.RegisterDefinitionAsync(definition, "Horse profile", ResourceType.Horse,
            1, "old", false);
        var resource = new ResourceKey(ResourceType.Horse, "JRA", $"horse-{Guid.NewGuid():N}");
        var now = DateTimeOffset.UtcNow.AddMinutes(-2);
        var receipt = await store.RequestAsync(resource, definition, 1, CollectionReason.Discovery, now,
            attributes: new Dictionary<string, string> { ["name"] = "アジアエクスプレス" });
        var lease = await store.AcquireAsync(receipt.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        await store.CompleteAttemptAsync(receipt.TaskId, lease.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.ResourceNotFound, "SubjectNotIdentified", "登録区分付き見出しです。",
                PageIdentification: "SubjectIdentification:ProfileNameMismatch"));
        await store.RegisterDefinitionAsync(definition, "Horse profile", ResourceType.Horse,
            2, "normalized", true);

        var first = await SubjectIdentificationAutoRecovery.RunOnceAsync(store);
        var second = await SubjectIdentificationAutoRecovery.RunOnceAsync(store);

        Assert.AreEqual(1, first.Recovered);
        Assert.AreEqual(0, second.Recovered);
        Assert.AreEqual(0, second.Examined);
        var tasks = await store.GetTasksAsync();
        Assert.HasCount(2, tasks);
        Assert.AreEqual(2, tasks.Single(x => x.TaskId != receipt.TaskId).RequestedRevision);
    }

    [TestMethod]
    public async Task InvalidPedigreeDescriptionIsSuppressedWithoutRecoveryTask()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        var store = application.Services.GetRequiredService<CollectionPlatformStore>();
        var definition = new CollectionDefinitionId("horse-profile");
        await store.RegisterDefinitionAsync(definition, "Horse profile", ResourceType.Horse,
            1, "old", false);
        var resource = new ResourceKey(ResourceType.Horse, "JRA", $"horse-{Guid.NewGuid():N}");
        var now = DateTimeOffset.UtcNow.AddMinutes(-2);
        var receipt = await store.RequestAsync(resource, definition, 1, CollectionReason.Discovery, now,
            attributes: new Dictionary<string, string> { ["name"] = "パネットーネ 産駒" });
        var lease = await store.AcquireAsync(receipt.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        await store.CompleteAttemptAsync(receipt.TaskId, lease.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.ResourceNotFound, "SubjectNotIdentified", "候補なし",
                PageIdentification: "SubjectIdentification:NoCandidate"));
        await store.RegisterDefinitionAsync(definition, "Horse profile", ResourceType.Horse,
            2, "normalized", true);

        var result = await SubjectIdentificationAutoRecovery.RunOnceAsync(store);

        Assert.AreEqual(1, result.Suppressed);
        Assert.HasCount(1, await store.GetTasksAsync());
        Assert.IsEmpty(await store.GetActionableFailureNotificationsAsync(DateTimeOffset.UtcNow, 10));
        var state = await store.GetStateAsync(resource, definition);
        Assert.AreEqual(CollectionStateStatus.Unavailable, state!.Status);
    }

    [TestMethod]
    public async Task NewRevisionRecoversStructuralFailureAndPreservesScheduling()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        var store = application.Services.GetRequiredService<CollectionPlatformStore>();
        var definition = new CollectionDefinitionId("trainer-profile");
        await store.RegisterDefinitionAsync(definition, "Trainer profile", ResourceType.Trainer,
            2, "old wait", false);
        var resource = new ResourceKey(ResourceType.Trainer, "JRA", $"trainer-{Guid.NewGuid():N}");
        var now = DateTimeOffset.UtcNow.AddMinutes(-2);
        var receipt = await store.RequestAsync(resource, definition, 2, CollectionReason.Discovery, now,
            CollectionLane.Background, 40,
            attributes: new Dictionary<string, string> { ["name"] = "テスト調教師" });
        var lease = await store.AcquireAsync(receipt.TaskId, 1, now, TimeSpan.FromMinutes(5));
        await store.CompleteAttemptAsync(receipt.TaskId, lease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "JraCollectionException",
                "調教師情報の見出しを確認できません。"));
        await store.SetPausedAsync(false, null, now.AddSeconds(2));
        await store.RegisterDefinitionAsync(definition, "Trainer profile", ResourceType.Trainer,
            3, "semantic readiness", true);

        var result = await SubjectIdentificationAutoRecovery.RunOnceAsync(store);

        Assert.AreEqual(1, result.Recovered);
        var recovered = (await store.GetTasksAsync()).Single(x => x.TaskId != receipt.TaskId);
        Assert.AreEqual(3, recovered.RequestedRevision);
        Assert.AreEqual(CollectionLane.Background, recovered.Lane);
        Assert.AreEqual(40, recovered.Priority);
    }
}
