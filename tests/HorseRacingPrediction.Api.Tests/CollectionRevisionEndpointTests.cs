using System.Net.Http.Json;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionRevisionEndpointTests
{
    [TestMethod]
    public async Task PreviewApplyExpandAndProgressUseStructuredSpecificResources()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"revision-api-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions { StateDirectory = directory }));
            var definition = new CollectionDefinitionId("horse-profile");
            var affected = new ResourceKey(ResourceType.Horse, "JRA", "H1");
            var unaffected = new ResourceKey(ResourceType.Horse, "JRA", "H2");
            await store.RegisterDefinitionAsync(definition, "Horse", ResourceType.Horse, 7, "baseline", false);
            await SeedCurrentAsync(store, affected, definition);
            await SeedCurrentAsync(store, unaffected, definition);

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(store);
            var app = builder.Build();
            app.MapCollectionPlatformEndpoints();
            await app.StartAsync();
            await using var lifetime = app;
            using var client = app.GetTestClient();
            var impact = new RevisionImpactRequest(RevisionImpactScopeType.SpecificResources, [affected]);

            var previewResponse = await client.PostAsJsonAsync("api/admin/collection/revisions/preview",
                new RevisionImpactPreviewRequest(definition.Value, 8, impact));
            previewResponse.EnsureSuccessStatusCode();
            var preview = await previewResponse.Content.ReadFromJsonAsync<RevisionImpactPreview>();
            Assert.IsNotNull(preview);
            CollectionAssert.AreEquivalent(new[] { affected.Normalize() }, preview.AffectedResources.ToArray());

            (await client.PostAsJsonAsync("api/admin/collection/revisions/apply",
                new ApplyCollectionRevisionRequest(definition.Value, 8, "specific fix", impact)))
                .EnsureSuccessStatusCode();
            (await client.PostAsJsonAsync($"api/admin/collection/revisions/{definition.Value}/8/recollect",
                new RevisionRecollectionRequest())).EnsureSuccessStatusCode();
            var progress = await client.GetFromJsonAsync<RevisionRecollectionProgress>(
                $"api/admin/collection/revisions/{definition.Value}/8/progress");

            Assert.IsNotNull(progress);
            Assert.AreEqual(1, progress.Affected);
            Assert.AreEqual(1, progress.Pending);
            Assert.AreEqual(8, (await store.GetStateAsync(affected, definition))!.RequiredRevision);
            Assert.AreEqual(7, (await store.GetStateAsync(unaffected, definition))!.RequiredRevision);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task SeedCurrentAsync(CollectionPlatformStore store, ResourceKey resource,
        CollectionDefinitionId definition)
    {
        var now = new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);
        var receipt = await store.RequestAsync(resource, definition, 7, CollectionReason.Initial, now);
        var lease = await store.AcquireAsync(receipt.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId, lease.LeaseToken, now,
            new(CollectionAttemptResult.Succeeded)));
    }
}
