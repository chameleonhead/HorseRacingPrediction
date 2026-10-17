using System.Net;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web.ApiBrowsing;
using HorseRacingPrediction.Contracts.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityMigrationSchemaDiagnosticsClientTests
{
    [TestMethod]
    public async Task AdminClientReadsAuthenticatedSchemaDiagnosticsOverHttp()
    {
        var expected = new HorseIdentityMigrationSchemaDiagnostics(DateTimeOffset.Parse("2026-10-17T00:00:00Z"),
            false,
            [new(HorseIdentityMigrationSchemaSurface.CollectionPlatform,
                HorseIdentityMigrationSchemaStatus.UnknownShape, 26, 0, new string('A', 64), [25],
                [new(HorseIdentityMigrationSchemaDifferenceKind.MigrationHistory,
                    HorseIdentityMigrationSchemaObjectKind.Table, "collection_schema_history")])]);
        var sawApiKey = false;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.Configure<ApiKeyOptions>(options =>
        {
            options.HeaderName = "X-Api-Key";
            options.Key = TestApplicationFactory.TestApiKey;
        });
        builder.Services.AddSingleton<ApiKeyEndpointFilter>();
        await using var app = builder.Build();
        app.MapGet("/api/v2/admin/horse-id-migration-schema-diagnostics", (HttpRequest request) =>
        {
            sawApiKey = request.Headers["X-Api-Key"] == TestApplicationFactory.TestApiKey;
            return Results.Ok(expected);
        }).AddEndpointFilter<ApiKeyEndpointFilter>();
        await app.StartAsync();

        using var http = app.GetTestClient();
        var client = new AdminApiClient(http,
            new AdminApiBaseAddressResolver(app.Services.GetRequiredService<IServer>()),
            Options.Create(new ApiKeyOptions { HeaderName = "X-Api-Key", Key = TestApplicationFactory.TestApiKey }));
        var observed = await client.GetHorseIdentityMigrationSchemaDiagnosticsAsync();

        Assert.IsTrue(sawApiKey);
        Assert.IsNotNull(observed);
        Assert.AreEqual(expected.IsComplete, observed.IsComplete);
        Assert.AreEqual(expected.Surfaces[0].Surface, observed.Surfaces[0].Surface);
        Assert.AreEqual(expected.Surfaces[0].Status, observed.Surfaces[0].Status);
        Assert.AreEqual(expected.Surfaces[0].MigrationHistoryVersions[0],
            observed.Surfaces[0].MigrationHistoryVersions[0]);
        Assert.AreEqual(expected.Surfaces[0].Differences[0], observed.Surfaces[0].Differences[0]);

        using var anonymousHttp = app.GetTestClient();
        var anonymousClient = new AdminApiClient(anonymousHttp,
            new AdminApiBaseAddressResolver(app.Services.GetRequiredService<IServer>()),
            Options.Create(new ApiKeyOptions { HeaderName = "X-Api-Key" }));
        var failure = await Assert.ThrowsAsync<HttpRequestException>(
            () => anonymousClient.GetHorseIdentityMigrationSchemaDiagnosticsAsync());
        Assert.AreEqual(HttpStatusCode.Unauthorized, failure.StatusCode);
    }
}
