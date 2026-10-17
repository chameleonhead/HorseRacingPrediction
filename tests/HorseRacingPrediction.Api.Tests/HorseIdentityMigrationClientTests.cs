using System.Net;
using System.Text.Json;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web.ApiBrowsing;
using HorseRacingPrediction.Contracts.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityMigrationClientTests
{
    [TestMethod]
    public async Task AdminClientUsesApiKeyForPreviewStageAndReadOnlyStatusContracts()
    {
        var manifest = new HorseIdentityMigrationManifest(
        [new HorseIdentityMigrationManifestEntry(
            "horse-11111111-1111-4111-8111-111111111111", "jra-horse:2024100828", true)]);
        var preview = new HorseIdentityMigrationPreview(true, new string('A', 64), [], [], [], [], [], 1, 64);
        var operationId = Guid.NewGuid();
        var requestKey = Guid.NewGuid();
        var response = new HorseIdentityMigrationOperationResponse(operationId, "AwaitingActions", "None",
            DateTimeOffset.Parse("2026-10-15T00:00:00Z"), DateTimeOffset.Parse("2026-10-15T00:00:00Z"), []);
        HorseIdentityMigrationManifest? receivedPreviewManifest = null;
        HorseIdentityMigrationStageRequest? receivedStage = null;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.Configure<ApiKeyOptions>(options =>
        {
            options.HeaderName = "X-Api-Key";
            options.Key = TestApplicationFactory.TestApiKey;
        });
        builder.Services.AddSingleton<ApiKeyEndpointFilter>();
        await using var app = builder.Build();
        var routes = app.MapGroup("/api").AddEndpointFilter<ApiKeyEndpointFilter>();
        routes.MapPost("/v2/admin/horse-id-migration-previews", async (HttpRequest request) =>
        {
            receivedPreviewManifest = await JsonSerializer.DeserializeAsync<HorseIdentityMigrationManifest>(request.Body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return Results.Ok(preview);
        });
        routes.MapPost("/v2/admin/horse-id-migration-operations", async (HttpRequest request) =>
        {
            receivedStage = await JsonSerializer.DeserializeAsync<HorseIdentityMigrationStageRequest>(request.Body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return Results.Created($"/api/v2/admin/horse-id-migration-operations/{operationId:D}", response);
        });
        routes.MapGet($"/v2/admin/horse-id-migration-operations/{operationId:D}", () => Results.Ok(response));
        routes.MapGet($"/v2/admin/horse-id-migration-requests/{requestKey:D}", () => Results.Ok(response));
        await app.StartAsync();

        using var http = app.GetTestClient();
        var client = CreateClient(http, app.Services.GetRequiredService<IServer>(), includeApiKey: true);
        var observedPreview = await client.PreviewHorseIdentityMigrationAsync(manifest);
        Assert.IsNotNull(observedPreview);
        Assert.AreEqual(new string('A', 64), observedPreview.Fingerprint);
        Assert.AreEqual(manifest.Entries![0], receivedPreviewManifest!.Entries![0]);

        var observedStage = await client.StageHorseIdentityMigrationAsync(manifest, observedPreview.Fingerprint!);
        Assert.IsNotNull(observedStage);
        Assert.AreEqual(operationId, observedStage.OperationId);
        Assert.AreEqual("AwaitingActions", observedStage.Status);
        Assert.AreEqual(observedPreview.Fingerprint, receivedStage!.PreviewFingerprint);
        Assert.AreEqual(manifest.Entries![0], receivedStage.Manifest!.Entries![0]);

        var observedStableStage = await client.StageHorseIdentityMigrationAsync(
            manifest, observedPreview.Fingerprint!, requestKey);
        Assert.IsNotNull(observedStableStage);
        Assert.AreEqual(operationId, observedStableStage.OperationId);
        Assert.AreEqual(requestKey, receivedStage.RequestKey);

        var observedRequest = await client.GetHorseIdentityMigrationRequestAsync(requestKey);
        Assert.IsNotNull(observedRequest);
        Assert.AreEqual(operationId, observedRequest.OperationId);

        var observedStatus = await client.GetHorseIdentityMigrationOperationAsync(operationId);
        Assert.IsNotNull(observedStatus);
        Assert.AreEqual(operationId, observedStatus.OperationId);

        using var anonymousHttp = app.GetTestClient();
        var anonymousClient = CreateClient(anonymousHttp, app.Services.GetRequiredService<IServer>(), includeApiKey: false);
        var unauthorized = await Assert.ThrowsAsync<HttpRequestException>(
            () => anonymousClient.PreviewHorseIdentityMigrationAsync(manifest));
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    private static AdminApiClient CreateClient(HttpClient http, IServer server, bool includeApiKey) => new(
        http,
        new AdminApiBaseAddressResolver(server),
        Options.Create(new ApiKeyOptions
        {
            HeaderName = "X-Api-Key",
            Key = includeApiKey ? TestApplicationFactory.TestApiKey : null,
        }));
}
