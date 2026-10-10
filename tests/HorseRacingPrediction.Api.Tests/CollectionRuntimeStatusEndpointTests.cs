using HorseRacingPrediction.Api;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Api.Endpoints.Collection;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Contracts.Collection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionRuntimeStatusEndpointTests
{
    [TestMethod]
    public async Task RuntimeStatusEndpoint_RequiresApiKeyAndReturnsMemorySnapshotWithoutCaching()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.Configure<ApiKeyOptions>(options =>
        {
            options.HeaderName = "X-Api-Key";
            options.Key = TestApplicationFactory.TestApiKey;
        });
        builder.Services.AddSingleton(CreateRecorder());

        var app = builder.Build();
        app.UseApiKeyProtection();
        GetCollectionRuntimeStatusEndpoint.Map(app);
        await app.StartAsync();
        await using var lifetime = app;
        using var client = app.GetTestClient();

        using var unauthorized = await client.GetAsync("/api/v2/admin/collection/operations/runtime-status");
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        using var response = await client.GetAsync("/api/v2/admin/collection/operations/runtime-status");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("no-store", response.Headers.CacheControl?.ToString());
        var payload = await response.Content.ReadFromJsonAsync<GetCollectionRuntimeStatusResponse>();
        Assert.IsNotNull(payload);
        Assert.AreNotEqual(Guid.Empty, payload.Runtime.InstanceId);
        Assert.HasCount(8, payload.Runtime.Actions);
        CollectionRuntimeAction[] expected = Enum.GetValues<CollectionRuntimeAction>();
        CollectionRuntimeAction[] actual = payload.Runtime.Actions.Select(action => action.Action).ToArray();
        CollectionAssert.AreEqual(expected, actual);
        Assert.IsTrue(payload.Runtime.Actions.All(action => action.State == CollectionRuntimeState.NotObserved));
    }

    private static CollectionRuntimeStatusRecorder CreateRecorder() => new(
        Enum.GetValues<CollectionRuntimeAction>().Select(action => new CollectionRuntimeActionConfiguration(
            action, Enabled: true,
            EffectiveInterval: action == CollectionRuntimeAction.MetricDelivery ? null : TimeSpan.FromSeconds(30))));
}
