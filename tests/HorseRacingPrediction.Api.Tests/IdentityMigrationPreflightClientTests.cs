using System.Net;
using System.Text.Json;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web.ApiBrowsing;
using HorseRacingPrediction.Contracts.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class IdentityMigrationPreflightClientTests
{
    [TestMethod]
    public async Task AdminClient_UsesEscapedGetCursorAndPreservesWrappedRepeatedPhaseEvidence()
    {
        var page = CreatePage();
        var builder = CreateBuilder();
        string? receivedCursor = null;
        var receivedBody = false;
        builder.Services.Configure<ApiKeyOptions>(options =>
        {
            options.HeaderName = "X-Api-Key";
            options.Key = TestApplicationFactory.TestApiKey;
        });
        builder.Services.AddSingleton<ApiKeyEndpointFilter>();
        await using var app = builder.Build();
        var adminRead = app.MapGroup("/api").AddEndpointFilter<ApiKeyEndpointFilter>();
        adminRead.MapGet("/v2/admin/identity-migration-preflight", (HttpRequest request) =>
        {
            receivedCursor = request.Query["cursor"].FirstOrDefault();
            receivedBody = request.ContentLength is > 0;
            return Results.Ok(new IdentityMigrationPreflightResponse(page));
        });
        await app.StartAsync();

        using var http = app.GetTestClient();
        var client = CreateClient(http, app.Services.GetRequiredService<IServer>(), includeApiKey: true);
        const string opaqueCursor = "protected + token/%2F";
        var result = await client.GetIdentityMigrationPreflightAsync(opaqueCursor);

        Assert.IsNotNull(result);
        Assert.AreEqual(opaqueCursor, receivedCursor);
        Assert.IsFalse(receivedBody);
        Assert.AreEqual(IdentityMigrationPreflightPhase.HorseRoots, result.Phase);
        Assert.AreEqual(IdentityMigrationPreflightPageState.InProgress, result.State);
        Assert.AreEqual("opaque-next", result.NextCursor);
        Assert.AreEqual(2, result.Evidence.Count);
        Assert.AreEqual(result.Evidence[0].HorseId, result.Evidence[1].HorseId);
        Assert.AreNotEqual(result.Evidence[0].Phase, result.Evidence[1].Phase);
        Assert.AreEqual("initial name", result.Evidence[0].InitialRegisteredName);
        Assert.AreEqual("jra-horse:2024108210", result.Evidence[0].CanonicalJraIdentity);
        Assert.AreEqual("race-00000000-0000-0000-0000-000000000001", result.Evidence[0].RelatedRaces[0].RaceId);
        Assert.AreEqual(9, result.Evidence[0].RelatedRaces[0].HorseNumber);
    }

    [TestMethod]
    public async Task AdminClient_ReturnsNullForMissingAndNoContentAndPreservesHttpFailureStatus()
    {
        var status = HttpStatusCode.NotFound;
        var builder = CreateBuilder();
        builder.Services.Configure<ApiKeyOptions>(options =>
        {
            options.HeaderName = "X-Api-Key";
            options.Key = TestApplicationFactory.TestApiKey;
        });
        builder.Services.AddSingleton<ApiKeyEndpointFilter>();
        await using var app = builder.Build();
        var adminRead = app.MapGroup("/api").AddEndpointFilter<ApiKeyEndpointFilter>();
        adminRead.MapGet("/v2/admin/identity-migration-preflight", () => Results.StatusCode((int)status));
        await app.StartAsync();

        using var http = app.GetTestClient();
        var client = CreateClient(http, app.Services.GetRequiredService<IServer>(), includeApiKey: true);
        Assert.IsNull(await client.GetIdentityMigrationPreflightAsync(null));
        status = HttpStatusCode.NoContent;
        Assert.IsNull(await client.GetIdentityMigrationPreflightAsync(null));

        foreach (var failure in new[]
                 { HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable })
        {
            status = failure;
            var exception = await Assert.ThrowsAsync<HttpRequestException>(
                () => client.GetIdentityMigrationPreflightAsync(null));
            Assert.AreEqual(failure, exception.StatusCode);
        }

        using var unauthenticatedHttp = app.GetTestClient();
        var unauthenticatedClient = CreateClient(unauthenticatedHttp,
            app.Services.GetRequiredService<IServer>(), includeApiKey: false);
        var authException = await Assert.ThrowsAsync<HttpRequestException>(
            () => unauthenticatedClient.GetIdentityMigrationPreflightAsync(null));
        Assert.AreEqual(HttpStatusCode.Unauthorized, authException.StatusCode);
    }

    [TestMethod]
    public async Task AdminClient_PropagatesCancellationWhileRequestIsInFlight()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = CreateBuilder();
        builder.Services.Configure<ApiKeyOptions>(options =>
        {
            options.HeaderName = "X-Api-Key";
            options.Key = TestApplicationFactory.TestApiKey;
        });
        builder.Services.AddSingleton<ApiKeyEndpointFilter>();
        await using var app = builder.Build();
        var adminRead = app.MapGroup("/api").AddEndpointFilter<ApiKeyEndpointFilter>();
        adminRead.MapGet("/v2/admin/identity-migration-preflight", async (CancellationToken token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Results.NoContent();
        });
        await app.StartAsync();

        using var http = app.GetTestClient();
        var client = CreateClient(http, app.Services.GetRequiredService<IServer>(), includeApiKey: true);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var request = client.GetIdentityMigrationPreflightAsync(null, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => request);
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        return builder;
    }

    private static AdminApiClient CreateClient(HttpClient http, IServer server, bool includeApiKey) => new(
        http,
        new AdminApiBaseAddressResolver(server),
        Options.Create(new ApiKeyOptions
        {
            HeaderName = "X-Api-Key",
            Key = includeApiKey ? TestApplicationFactory.TestApiKey : null,
        }));

    private static IdentityMigrationPreflightPage CreatePage()
    {
        var evidence = new IdentityMigrationPreflightHorseEvidence(
            "horse-00000000-0000-0000-0000-000000000001",
            IdentityMigrationPreflightPhase.HorseRoots,
            IdentityMigrationPreflightObservationStatus.Unverifiable,
            "current name", "current-key", new DateOnly(2020, 2, 3),
            "initial name", "initial-key", new DateOnly(2020, 2, 3),
            "jra-horse:2024108210", IdentityMigrationPreflightObservationStatus.Observed,
            [new("race-00000000-0000-0000-0000-000000000001", 9)], 1, false);
        return new(
            IdentityMigrationPreflightPageState.InProgress,
            IdentityMigrationPreflightPhase.HorseRoots,
            new DateTimeOffset(2026, 10, 12, 4, 5, 6, TimeSpan.Zero),
            1,
            new(1, 0, 0, 0, 0, 0, 0, 0, 1, 0),
            [new(IdentityMigrationPreflightSurface.EventStore, IdentityMigrationPreflightObservationStatus.Observed,
                new DateTimeOffset(2026, 10, 12, 4, 5, 6, TimeSpan.Zero), 4, 0)],
            [evidence, evidence with { Phase = IdentityMigrationPreflightPhase.HorseEvents }],
            "opaque-next",
            true);
    }
}
