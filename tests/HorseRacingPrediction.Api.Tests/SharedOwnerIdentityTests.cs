using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Scraping.Jra;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class SharedOwnerIdentityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task BulkCard_OwnerTasksUseSharedCanonicalIdentity_AndUnrelatedOwnersRemainDistinct()
    {
        var (app, client) = await CreateAsync();
        await using var application = app;
        using var http = client;

        var canonicalName = "(株)ノルマンディーRACING";
        var equivalentNames = new[]
        {
            canonicalName,
            "（株） ノルマンディーｒａｃｉｎｇ",
            "株式会社ノルマンディーracing"
        };
        var unrelatedName = "(株)ノルマンディーSPORTS";
        var request = Card(Guid.NewGuid().ToString("N"),
            [equivalentNames[0], equivalentNames[1], equivalentNames[2], unrelatedName]);

        var response = await http.PostAsJsonAsync("/api/races/result-bulk", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>(JsonOptions);
        Assert.IsNotNull(body);
        Assert.IsTrue(body.CorePersisted, string.Join(";", body.Errors));

        var tasks = await OwnerTasksAsync(http);
        Assert.HasCount(2, tasks);
        var expectedCanonicalId = OwnerIdentityContract.CreateId(canonicalName);
        var expectedUnrelatedId = OwnerIdentityContract.CreateId(unrelatedName);
        CollectionAssert.AreEquivalent(new[] { expectedCanonicalId, expectedUnrelatedId },
            tasks.Select(x => x.Resource.Id).ToArray());
        Assert.AreNotEqual(expectedCanonicalId, expectedUnrelatedId);

        await ExecuteOwnerTasksAsync(http, tasks);
        var verifier = new OwnerIdentityApiClient(http);
        Assert.IsTrue(await verifier.ExistsAsync(expectedCanonicalId, CancellationToken.None));
        Assert.IsTrue(await verifier.ExistsAsync(expectedUnrelatedId, CancellationToken.None));
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await http.GetAsync($"/api/owners/{OwnerIdentityContract.CreateId("never-owner")}"))
            .StatusCode);
    }

    [TestMethod]
    public async Task BulkCard_AfterOwnerMerge_ResolvesSourceAliasToTarget_WithoutDuplicateOwner()
    {
        var (app, client) = await CreateAsync();
        await using var application = app;
        using var http = client;

        var key = Guid.NewGuid().ToString("N");
        var targetName = $"統合先馬主{key}";
        var sourceName = $"統合元馬主{key}";
        var first = await SaveCardAsync(http, $"first-{key}", [targetName, sourceName]);
        var initialTasks = await OwnerTasksAsync(http);
        Assert.HasCount(2, initialTasks);
        await ExecuteOwnerTasksAsync(http, initialTasks);

        var owners = await http.GetFromJsonAsync<IReadOnlyList<OwnerSummaryResponse>>(
            $"/api/owners?query={key}", JsonOptions);
        Assert.IsNotNull(owners);
        var target = owners.Single(x => x.DisplayName == targetName);
        var source = owners.Single(x => x.DisplayName == sourceName);
        var merge = await http.PostAsJsonAsync($"/api/owners/{target.OwnerId}/merge",
            new MergeOwnerRequest(source.OwnerId, "同一人物の別表記"), JsonOptions);
        Assert.AreEqual(HttpStatusCode.NoContent, merge.StatusCode);

        var second = await SaveCardAsync(http, $"second-{key}", [sourceName]);
        var secondTasks = await OwnerTasksAsync(http);
        var secondTask = secondTasks.Single(x => x.Resource.Id == target.OwnerId);
        Assert.AreEqual(target.OwnerId, secondTask.Resource.Id);
        Assert.AreEqual(1, (await http.GetFromJsonAsync<IReadOnlyList<OwnerSummaryResponse>>(
            $"/api/owners?query={key}", JsonOptions))!.Count);

        var merged = await http.GetFromJsonAsync<OwnerDetailResponse>(
            $"/api/owners/{target.OwnerId}", JsonOptions);
        Assert.IsNotNull(merged);
        Assert.IsTrue(merged.Summary.NameVariants.Contains(targetName));
        Assert.IsTrue(merged.Summary.NameVariants.Contains(sourceName));
        Assert.AreEqual(1, (await http.GetFromJsonAsync<IReadOnlyList<OwnerSummaryResponse>>(
            $"/api/owners?query={key}", JsonOptions))!.Count);

        if (secondTask.Status == CollectionTaskStatus.Ready)
            await ExecuteOwnerTasksAsync(http, [secondTask]);
        var verifier = new OwnerIdentityApiClient(http);
        Assert.IsTrue(await verifier.ExistsAsync(target.OwnerId, CancellationToken.None));
        Assert.IsFalse(await verifier.ExistsAsync(source.OwnerId, CancellationToken.None));
        Assert.AreNotEqual(first.RaceId, second.RaceId);
    }

    private static async Task<(WebApplication App, HttpClient Client)> CreateAsync()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = app.Services.GetRequiredService<CollectionPlatformStore>();
        await store.RegisterDefinitionAsync(new("horse-profile"), "Horse", CollectionResourceType.Horse, 4, "test", true);
        await store.RegisterDefinitionAsync(new("jockey-profile"), "Jockey", CollectionResourceType.Jockey, 3, "test", true);
        await store.RegisterDefinitionAsync(new("trainer-profile"), "Trainer", CollectionResourceType.Trainer, 3, "test", true);
        await store.RegisterDefinitionAsync(new("owner-identity"), "Owner", CollectionResourceType.Owner, 1, "test", true);
        return (app, client);
    }

    private static DeclareRaceResultBulkRequest Card(string course, IReadOnlyList<string> owners) =>
        new(new(2036, 9, 27), course, 1, "馬主主体ID検証", EntryCount: owners.Count,
            IsRaceCard: true,
            Entries: owners.Select((owner, index) => new RaceResultEntryBulkDto(index + 1, null, null, null,
                null, null, null, HorseName: $"検証馬{course}-{index}", JockeyName: $"騎手{course}-{index}",
                TrainerName: $"調教師{course}-{index}", OwnerName: owner,
                HorseSourceIdentity: $"https://www.jra.go.jp/JRADB/accessU.html?CNAME=owner-{course}-{index}"))
                .ToArray());

    private static async Task<DeclareRaceResultBulkResponse> SaveCardAsync(HttpClient http, string course,
        IReadOnlyList<string> owners)
    {
        var response = await http.PostAsJsonAsync("/api/races/result-bulk", Card(course, owners), JsonOptions);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>(JsonOptions);
        Assert.IsNotNull(body);
        Assert.IsTrue(body.CorePersisted, string.Join(";", body.Errors));
        return body;
    }

    private static async Task<IReadOnlyList<CollectionTaskSummary>> OwnerTasksAsync(HttpClient http)
    {
        var tasks = await http.GetFromJsonAsync<CollectionTaskPage>(
            "/api/v2/admin/collection/tasks?limit=1000", JsonOptions);
        Assert.IsNotNull(tasks);
        return tasks.Items.Where(x => x.Definition.Value == "owner-identity").ToArray();
    }

    private static async Task ExecuteOwnerTasksAsync(HttpClient http,
        IReadOnlyList<CollectionTaskSummary> tasks)
    {
        var handler = new JraSubjectProfileCollectionHandler(
            JraSubjectCollectionDefinitions.For(CollectionResourceType.Owner), new ThrowingSessionFactory(),
            new JraSubjectProfileApiClient(http), ownerIdentities: new OwnerIdentityApiClient(http));
        var worker = new CollectionPlatformWorkerClient(http,
            new CollectionDefinitionHandlerRegistry([handler]));
        foreach (var task in tasks)
            await worker.ExecuteAsync(new(task.TaskId, task.Status == CollectionTaskStatus.Ready ? 1 : 1),
                CancellationToken.None);
    }

    private sealed class ThrowingSessionFactory : IJraSessionFactory
    {
        public Task<JraSession> CreateAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Owner identity collection must not create a JRA session.");
    }
}
