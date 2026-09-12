using System.Net;
using System.Net.Http.Json;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Text;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class JraExplicitUrlCollectionTests
{
    private const string ResultUrl =
        "https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1006202604011120260905/2F";

    [TestMethod]
    public void Resolve_ResultUrl_IdentifiesCanonicalResourceAndDefinition()
    {
        var result = JraExplicitUrlResolver.Resolve(
            "https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1006202604011120260905/2F");

        Assert.IsTrue(result.Identified);
        Assert.AreEqual(new ResourceKey(ResourceType.RaceResult, "JRA", "20260905:Nakayama:11"), result.Resource);
        Assert.AreEqual(new CollectionDefinitionId("race-result"), result.Definition);
        Assert.AreEqual(new DateOnly(2026, 9, 5), result.EffectiveDate);
        Assert.AreEqual("中山", result.Attributes["course"]);
        Assert.AreEqual("11", result.Attributes["number"]);
    }

    [TestMethod]
    public void Resolve_UnsupportedUrl_ReturnsExplicitUnidentifiedResult()
    {
        var result = JraExplicitUrlResolver.Resolve("https://www.jra.go.jp/JRADB/accessS.html?CNAME=unknown");

        Assert.IsFalse(result.Identified);
        Assert.AreEqual("UnidentifiedExplicitLocation", result.ErrorCode);
        Assert.IsNull(result.Resource);
        Assert.IsNull(result.Definition);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("not-a-url")]
    [DataRow("/JRADB/accessS.html?CNAME=pw01sde1006202604011120260905/2F")]
    [DataRow("http://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1006202604011120260905/2F")]
    [DataRow("https://example.test/JRADB/accessS.html?CNAME=pw01sde1006202604011120260905/2F")]
    [DataRow("https://www.jra.go.jp.evil.test/JRADB/accessS.html?CNAME=pw01sde1006202604011120260905/2F")]
    [DataRow("https://www.jra.go.jp:444/JRADB/accessS.html?CNAME=pw01sde1006202604011120260905/2F")]
    [DataRow("https://user@www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1006202604011120260905/2F")]
    [DataRow("https://www.jra.go.jp/JRADB/accessS.html")]
    [DataRow("https://www.jra.go.jp/JRADB/accessS.html?CNAME=one&CNAME=two")]
    [DataRow("https://www.jra.go.jp/JRADB/accessS.html?cname=one&CNAME=two")]
    [DataRow("https://www.jra.go.jp/JRADB/accessS.html?CNAME=%ZZ")]
    [DataRow("https://www.jra.go.jp/JRADB/accessS.html?CNAME=unknown")]
    [DataRow("https://www.jra.go.jp/not-race?CNAME=pw01sde1006202604011120260905/2F")]
    [DataRow("https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01sde1006202604011120260905/2F")]
    [DataRow("https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1000202604011120260905/2F")]
    [DataRow("https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1011202604011120260905/2F")]
    [DataRow("https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1006202604010020260905/2F")]
    [DataRow("https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1006202604011320260905/2F")]
    [DataRow("https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1006202604011120260229/2F")]
    public void Resolve_InvalidInput_ReturnsExplicitUnidentifiedResult(string? url)
    {
        var result = JraExplicitUrlResolver.Resolve(url);

        Assert.IsFalse(result.Identified);
        Assert.AreEqual("UnidentifiedExplicitLocation", result.ErrorCode);
        Assert.IsNull(result.Resource);
        Assert.IsNull(result.Definition);
    }

    [TestMethod]
    public void Resolve_ExtremelyLongUrl_ReturnsExplicitUnidentifiedResult()
    {
        var result = JraExplicitUrlResolver.Resolve(ResultUrl + new string('x', 4096));

        Assert.IsFalse(result.Identified);
        Assert.AreEqual("UnidentifiedExplicitLocation", result.ErrorCode);
    }

    [TestMethod]
    [DataRow("https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1001202604010120240229/2F", ResourceType.RaceResult)]
    [DataRow("https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1010202604011220261231/2F", ResourceType.RaceResult)]
    [DataRow("https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01dde1006202604010120260101/25", ResourceType.RaceCard)]
    public void Resolve_ValidBoundaryInput_IsIdentified(string url, ResourceType expectedType)
    {
        var result = JraExplicitUrlResolver.Resolve(url);

        Assert.IsTrue(result.Identified);
        Assert.AreEqual(expectedType, result.Resource!.Value.Type);
    }

    [TestMethod]
    public void Resolve_RaceCardUrl_UsesRaceCardDefinition()
    {
        var result = JraExplicitUrlResolver.Resolve(
            "https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01dde1006202604020520260906/25");

        Assert.IsTrue(result.Identified);
        Assert.AreEqual(new ResourceKey(ResourceType.RaceCard, "JRA", "20260906:Nakayama:5"), result.Resource);
        Assert.AreEqual(new CollectionDefinitionId("race-card"), result.Definition);
    }

    [TestMethod]
    public async Task RequestByUrl_Unidentified_DoesNotCreateAnonymousTask()
    {
        var (app, client) = await CreateApplicationAsync();
        await using var application = app;
        using var http = client;

        var before = await http.GetFromJsonAsync<IReadOnlyList<CollectionTaskSummary>>(
            "/api/admin/collection/tasks?limit=100");
        using var response = await http.PostAsJsonAsync("/api/admin/collection/requests/by-url",
            new CreateExplicitUrlCollectionRequest("https://example.test/not-jra"));

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ExplicitUrlCollectionResult>();
        Assert.IsNotNull(error);
        Assert.AreEqual("UnidentifiedExplicitLocation", error.ErrorCode);
        var after = await http.GetFromJsonAsync<IReadOnlyList<CollectionTaskSummary>>(
            "/api/admin/collection/tasks?limit=100");
        Assert.AreEqual(before!.Count, after!.Count);
    }

    [TestMethod]
    public async Task RequestByUrl_InvalidInputs_Return422WithoutPersistingCollectionData()
    {
        var (app, client, directory) = await CreateApplicationWithDirectoryAsync();
        await using var application = app;
        using var http = client;
        var invalidBodies = new[]
        {
            "{}",
            "{\"url\":null}",
            "{\"url\":\"\"}",
            "{\"url\":\"https://www.jra.go.jp/JRADB/accessS.html?CNAME=one&CNAME=two\"}",
            "{\"url\":\"https://www.jra.go.jp.evil.test/JRADB/accessS.html?CNAME=pw01sde1006202604011120260905/2F\"}",
            "{\"url\":\"https://www.jra.go.jp/JRADB/accessS.html?CNAME=%ZZ\"}",
        };

        foreach (var body in invalidBodies)
        {
            using var response = await http.PostAsync("/api/admin/collection/requests/by-url",
                new StringContent(body, Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode, body);
            var error = await response.Content.ReadFromJsonAsync<ExplicitUrlCollectionResult>();
            Assert.AreEqual("UnidentifiedExplicitLocation", error?.ErrorCode, body);
        }

        var options = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
            .UseSqlite($"Data Source={Path.Combine(directory, "collection-platform.db")};Pooling=False").Options;
        await using var db = new CollectionPlatformDbContext(options);
        Assert.AreEqual(0, await db.Resources.CountAsync());
        Assert.AreEqual(0, await db.Requests.CountAsync());
        Assert.AreEqual(0, await db.Tasks.CountAsync());
    }

    [TestMethod]
    public async Task RequestByUrl_Identified_CreatesNormalCollectionRequest()
    {
        var (app, client) = await CreateApplicationAsync();
        await using var application = app;
        using var http = client;
        const string url = "https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1006202604011120260905/2F";

        using var response = await http.PostAsJsonAsync("/api/admin/collection/requests/by-url",
            new CreateExplicitUrlCollectionRequest(url));

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ExplicitUrlCollectionResult>();
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Identified);
        Assert.IsNotNull(result.Receipt);
        var tasks = await http.GetFromJsonAsync<IReadOnlyList<CollectionTaskSummary>>(
            "/api/admin/collection/tasks?limit=100");
        var task = tasks!.Single(x => x.Resource == result.Resource && x.Definition == result.Definition);
        Assert.AreEqual(2, task.RequestedRevision);
    }

    private static async Task<(WebApplication App, HttpClient Client)> CreateApplicationAsync()
    {
        var (app, client, _) = await CreateApplicationWithDirectoryAsync();
        return (app, client);
    }

    private static async Task<(WebApplication App, HttpClient Client, string Directory)> CreateApplicationWithDirectoryAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"explicit-url-api-{Guid.NewGuid():N}");
        var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            { StateDirectory = directory }));
        await store.RegisterDefinitionAsync(new("race-result"), "Race result", ResourceType.RaceResult,
            1, "initial", false);
        await store.RegisterDefinitionAsync(new("race-result"), "Race result", ResourceType.RaceResult,
            2, "current", false);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(store);
        var app = builder.Build();
        app.MapCollectionPlatformEndpoints();
        await app.StartAsync();
        return (app, app.GetTestClient(), directory);
    }
}
