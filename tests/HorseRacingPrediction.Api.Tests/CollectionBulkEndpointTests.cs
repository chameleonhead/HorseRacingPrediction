using EventFlow.EntityFramework;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionBulkEndpointTests
{
    [TestMethod]
    public async Task DateAndTrainerSelectionsUseDomainEntriesAndRequireMatchingPreview()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"bulk-api-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var collection = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            { StateDirectory = Path.Combine(directory, "collection") }));
            await collection.RegisterDefinitionAsync(new("horse-profile"), "Horse", CollectionResourceType.Horse,
                1, "initial", false);
            using var domain = new SqliteDbContextProvider($"Data Source={Path.Combine(directory, "domain.db")}");
            using (var db = domain.CreateContext())
            {
                db.Database.EnsureCreated();
                db.RacePredictionContexts.Add(CreateRace("R1", new(2026, 9, 10),
                    new("E1", "H1", 1, null, "T1", null, null, null, null, null, null, null),
                    new("E2", "H2", 2, null, "T2", null, null, null, null, null, null, null)));
                await db.SaveChangesAsync();
            }
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(collection);
            builder.Services.AddSingleton<IDbContextProvider<EventStoreDbContext>>(domain);
            var app = builder.Build();
            app.MapCollectionApiV2Endpoints();
            await app.StartAsync();
            await using var lifetime = app;
            using var client = app.GetTestClient();

            var dateRequest = new BulkCollectionOperationRequest("horse-profile", 1,
                CollectionReason.ManualRefresh, BulkCollectionSelection.HorsesRacedInDateRange,
                From: new(2026, 9, 10), To: new(2026, 9, 10));
            var previewResponse = await client.PostAsJsonAsync("api/v2/admin/collection/task-batch-previews", dateRequest);
            previewResponse.EnsureSuccessStatusCode();
            var preview = await previewResponse.Content.ReadFromJsonAsync<CollectionBulkPreview>();
            Assert.IsNotNull(preview);
            Assert.AreEqual(2, preview.TargetCount);

            var mismatch = dateRequest with { ExpectedResources = [new(CollectionResourceType.Horse, "JRA", "H1")] };
            Assert.AreEqual(HttpStatusCode.Conflict,
                (await client.PostAsJsonAsync("api/v2/admin/collection/task-batches",
                    ToPreviewSelection(mismatch))).StatusCode);
            Assert.IsEmpty(await collection.GetTasksAsync());
            var execute = dateRequest with { ExpectedResources = preview.Resources, BatchId = "manual:date" };
            var executionResponse = await client.PostAsJsonAsync("api/v2/admin/collection/task-batches",
                ToPreviewSelection(execute));
            executionResponse.EnsureSuccessStatusCode();
            var execution = await executionResponse.Content.ReadFromJsonAsync<CollectionTaskBatchSubmissionResponse>();
            Assert.AreEqual("PreviewSelection", execution?.Mode);
            Assert.IsNotNull(execution?.PreviewSelection);
            Assert.HasCount(2, await collection.GetTasksAsync());

            var trainerRequest = new BulkCollectionOperationRequest("horse-profile", 1,
                CollectionReason.ManualRefresh, BulkCollectionSelection.HorsesByTrainer, TrainerId: "T1");
            var trainerPreview = await (await client.PostAsJsonAsync(
                "api/v2/admin/collection/task-batch-previews", trainerRequest)).Content
                .ReadFromJsonAsync<CollectionBulkPreview>();
            Assert.IsNotNull(trainerPreview);
            CollectionAssert.AreEquivalent(new[] { new ResourceKey(CollectionResourceType.Horse, "JRA", "H1") },
                trainerPreview.Resources.ToArray());

            await collection.SuppressResourceAsync(new(CollectionResourceType.Horse, "JRA", "H1"),
                "Merged horse was deleted", "repair-1", DateTimeOffset.UtcNow);
            var suppressedDatePreview = await (await client.PostAsJsonAsync(
                "api/v2/admin/collection/task-batch-previews", dateRequest)).Content
                .ReadFromJsonAsync<CollectionBulkPreview>();
            Assert.IsNotNull(suppressedDatePreview);
            CollectionAssert.AreEquivalent(new[] { new ResourceKey(CollectionResourceType.Horse, "JRA", "H2") },
                suppressedDatePreview.Resources.ToArray());
            var explicitRequest = new BulkCollectionOperationRequest("horse-profile", 1,
                CollectionReason.ManualRefresh, BulkCollectionSelection.SpecificResources,
                Resources: [new(CollectionResourceType.Horse, "JRA", "H1"), new(CollectionResourceType.Horse, "JRA", "H2")]);
            var explicitPreview = await (await client.PostAsJsonAsync(
                "api/v2/admin/collection/task-batch-previews", explicitRequest)).Content
                .ReadFromJsonAsync<CollectionBulkPreview>();
            Assert.IsNotNull(explicitPreview);
            CollectionAssert.AreEquivalent(new[] { new ResourceKey(CollectionResourceType.Horse, "JRA", "H2") },
                explicitPreview.Resources.ToArray());
        }
        finally { Directory.Delete(directory, true); }
    }

    private static RacePredictionContextReadModel CreateRace(string raceId, DateOnly date,
        params Application.Queries.ReadModels.RacePredictionContextEntry[] entries)
    {
        var model = new RacePredictionContextReadModel();
        Set(model, nameof(model.RaceId), raceId);
        Set(model, nameof(model.RaceDate), (DateOnly?)date);
        Set(model, nameof(model.Entries), entries.ToList());
        return model;
    }

    private static CollectionTaskBatchRequest ToPreviewSelection(BulkCollectionOperationRequest request)
        => new("PreviewSelection", new(request.DefinitionId, request.RequestedRevision,
            request.Reason, request.Selection.ToString(), request.Provider, request.Resources,
            request.From, request.To, request.TrainerId, request.LastCollectedBefore,
            request.ExpectedResources, request.BatchId, request.Lane, request.Priority));

    private static void Set<T>(RacePredictionContextReadModel model, string property, T value) =>
        typeof(RacePredictionContextReadModel).GetProperty(property)!.SetValue(model, value);
}
