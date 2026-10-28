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
    public async Task CancelPreparationUsesBodylessPostAndPreservesFrozenHttpOutcomes()
    {
        var acceptedId = Guid.NewGuid();
        var missingId = Guid.NewGuid();
        var conflictId = Guid.NewGuid();
        var unavailableId = Guid.NewGuid();
        var malformedId = Guid.NewGuid();
        var receipt = new HorseIdentityMigrationOperationResponse(acceptedId, "AwaitingActions",
            "None", DateTimeOffset.Parse("2026-10-15T00:00:00Z"),
            DateTimeOffset.Parse("2026-10-15T00:01:00Z"), [])
        { ExecutionPhase = "CancellationRequested" };
        var requests = new List<(HttpMethod Method, string Path, long? ContentLength, string Body)>();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.MapPost("/api/v2/admin/horse-id-migration-operations/{operationId:guid}/cancel-preparation",
            async (Guid operationId, HttpRequest request) =>
            {
                using var body = new StreamReader(request.Body);
                requests.Add((HttpMethod.Post, request.Path.Value ?? string.Empty, request.ContentLength,
                    await body.ReadToEndAsync()));
                if (operationId == acceptedId)
                    return Results.Accepted($"/api/v2/admin/horse-id-migration-operations/{acceptedId:D}/cancel-preparation", receipt);
                if (operationId == malformedId)
                    return Results.Content("{", "application/json", statusCode: (int)HttpStatusCode.Accepted);
                return Results.StatusCode(operationId == conflictId
                    ? (int)HttpStatusCode.Conflict
                    : operationId == unavailableId
                        ? (int)HttpStatusCode.ServiceUnavailable
                        : (int)HttpStatusCode.NotFound);
            });
        await app.StartAsync();

        using var http = app.GetTestClient();
        var client = CreateClient(http, app.Services.GetRequiredService<IServer>(), includeApiKey: true);
        var accepted = await client.CancelHorseIdentityMigrationPreparationAsync(acceptedId);
        var missing = await client.CancelHorseIdentityMigrationPreparationAsync(missingId);
        var conflict = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.CancelHorseIdentityMigrationPreparationAsync(conflictId));
        var unavailable = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.CancelHorseIdentityMigrationPreparationAsync(unavailableId));
        var malformed = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.CancelHorseIdentityMigrationPreparationAsync(malformedId));
        Assert.IsNull(await client.CancelHorseIdentityMigrationPreparationAsync(Guid.Empty));

        Assert.AreEqual(acceptedId, accepted!.OperationId);
        Assert.AreEqual("CancellationRequested", accepted.ExecutionPhase);
        Assert.IsNull(missing);
        Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.AreEqual(HttpStatusCode.Accepted, malformed.StatusCode,
            "A malformed successful receipt remains an ambiguous response with its original status.");
        Assert.IsInstanceOfType<JsonException>(malformed.InnerException);
        Assert.AreEqual(5, requests.Count, "An empty operation ID must not send an HTTP request.");
        Assert.IsTrue(requests.All(request => request.Method == HttpMethod.Post
            && request.Path.EndsWith("/cancel-preparation", StringComparison.Ordinal)
            && request.ContentLength is null && request.Body.Length == 0),
            "Cancellation is a bodyless journal action; no manifest or command payload may be sent.");
        Assert.AreEqual($"/api/v2/admin/horse-id-migration-operations/{acceptedId:D}/cancel-preparation",
            requests[0].Path);
    }

    [TestMethod]
    public async Task AdminClientUsesApiKeyForPreviewStageAndReadOnlyStatusContracts()
    {
        var manifest = new HorseIdentityMigrationManifest(
        [new HorseIdentityMigrationManifestEntry(
            "horse-11111111-1111-4111-8111-111111111111", "jra-horse:2024100828", true)]);
        var preview = new HorseIdentityMigrationPreview(true, new string('A', 64), [], [], [], [], [], 1, 64)
        {
            InspectionDiagnostic = new(HorseIdentityMigrationInspectionPhase.TypedReferences,
                HorseIdentityMigrationInspectionResult.Running, HorseIdentityMigrationSchemaSurface.EventStore,
                "EventEntity", 1, 64, 12),
        };
        var operationId = Guid.NewGuid();
        var requestKey = Guid.NewGuid();
        var response = new HorseIdentityMigrationOperationResponse(operationId, "AwaitingActions", "None",
            DateTimeOffset.Parse("2026-10-15T00:00:00Z"), DateTimeOffset.Parse("2026-10-15T00:00:00Z"), []);
        HorseIdentityMigrationManifest? receivedPreviewManifest = null;
        HorseIdentityMigrationManifest? receivedTargetReviewManifest = null;
        HorseIdentityMigrationStageRequest? receivedStage = null;
        var executeCount = 0;
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
            var manifestBody = await JsonSerializer.DeserializeAsync<HorseIdentityMigrationManifest>(request.Body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (request.Query["kind"] == "TargetReviewV2")
            {
                receivedTargetReviewManifest = manifestBody;
                return Results.Ok(new HorseIdentityMigrationTargetReview(true, [],
                    [new("horse-11111111-1111-4111-8111-111111111111", "jra-horse:2024100828",
                        "horse-4f4ef3a7-9c3c-4878-b7d4-1689b1d2cced")],
                    [new("horse-11111111-1111-4111-8111-111111111111", "horse-4f4ef3a7-9c3c-4878-b7d4-1689b1d2cced",
                        HorseIdentityMigrationTargetReviewRootStatus.Present)]));
            }
            receivedPreviewManifest = manifestBody;
            return Results.Ok(preview);
        });
        routes.MapPost("/v2/admin/horse-id-migration-operations", async (HttpRequest request) =>
        {
            receivedStage = await JsonSerializer.DeserializeAsync<HorseIdentityMigrationStageRequest>(request.Body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return Results.Created($"/api/v2/admin/horse-id-migration-operations/{operationId:D}", response);
        });
        routes.MapGet($"/v2/admin/horse-id-migration-operations/{operationId:D}", () => Results.Ok(response));
        routes.MapPost($"/v2/admin/horse-id-migration-operations/{operationId:D}/execute", () =>
        {
            executeCount++;
            return Results.Accepted($"/api/v2/admin/horse-id-migration-operations/{operationId:D}", response);
        });
        routes.MapGet($"/v2/admin/horse-id-migration-requests/{requestKey:D}", () => Results.Ok(response));
        await app.StartAsync();

        using var http = app.GetTestClient();
        var client = CreateClient(http, app.Services.GetRequiredService<IServer>(), includeApiKey: true);
        var observedPreview = await client.PreviewHorseIdentityMigrationAsync(manifest);
        Assert.IsNotNull(observedPreview);
        Assert.AreEqual(new string('A', 64), observedPreview.Fingerprint);
        Assert.AreEqual(HorseIdentityMigrationInspectionResult.Running, observedPreview.InspectionDiagnostic?.Result);
        Assert.AreEqual("EventEntity", observedPreview.InspectionDiagnostic?.LastCompletedTable);
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

        var targetReview = await client.ReviewHorseIdentityMigrationTargetsAsync(manifest);
        Assert.IsNotNull(targetReview);
        Assert.IsTrue(targetReview.CanSavePending);
        Assert.AreEqual(manifest.Entries![0], receivedTargetReviewManifest!.Entries![0]);
        var pendingStage = await client.StagePendingHorseIdentityMigrationAsync(manifest, requestKey: Guid.NewGuid());
        Assert.IsNotNull(pendingStage);
        Assert.AreEqual(HorseIdentityMigrationAdmissionKind.PendingV2, receivedStage!.RequestKind);
        Assert.IsNull(receivedStage.PreviewFingerprint);
        Assert.AreEqual(manifest.Entries![0], receivedStage.Manifest!.Entries![0]);

        var observedRequest = await client.GetHorseIdentityMigrationRequestAsync(requestKey);
        Assert.IsNotNull(observedRequest);
        Assert.AreEqual(operationId, observedRequest.OperationId);

        var observedStatus = await client.GetHorseIdentityMigrationOperationAsync(operationId);
        Assert.IsNotNull(observedStatus);
        Assert.AreEqual(operationId, observedStatus.OperationId);

        var executed = await client.ExecuteHorseIdentityMigrationAsync(operationId);
        Assert.IsNotNull(executed);
        Assert.AreEqual(operationId, executed.OperationId);
        Assert.AreEqual(1, executeCount);
        Assert.IsNull(await client.ExecuteHorseIdentityMigrationAsync(Guid.Empty));

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
