using EventFlow.EntityFramework;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;

using HorseRacingPrediction.Contracts.Collection;

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
            builder.Services.AddSingleton<ICollectionDispatchTelemetry, NullCollectionDispatchTelemetry>();
            builder.Services.AddSingleton<IDbContextProvider<EventStoreDbContext>>(domain);
            var app = builder.Build();
            app.MapCollectionApiV2Endpoints();
            await app.StartAsync();
            await using var lifetime = app;
            using var client = app.GetTestClient();

            var dateRequest = new BulkCollectionOperationRequest("horse-profile", 1,
                CollectionReason.ManualRefresh, BulkCollectionSelection.HorsesRacedInDateRange,
                From: new(2026, 9, 10), To: new(2026, 9, 10));
            var previewResponse = await client.PostAsJsonAsync("api/v2/admin/collection/task-batch-previews",
                ToPreview(dateRequest));
            previewResponse.EnsureSuccessStatusCode();
            var previewEnvelope = await previewResponse.Content.ReadFromJsonAsync<PreviewCollectionTaskBatchResponse>();
            var preview = previewEnvelope?.Preview;
            Assert.IsNotNull(preview);
            Assert.AreEqual(2, preview.TargetCount);

            var mismatch = dateRequest with { ExpectedResources = [new(CollectionResourceType.Horse, "JRA", "H1")] };
            Assert.AreEqual(HttpStatusCode.Conflict,
                (await client.PostAsJsonAsync("api/v2/admin/collection/task-batches",
                    ToPreviewSelection(mismatch))).StatusCode);
            Assert.IsEmpty(await collection.GetTasksAsync());
            var execute = dateRequest with
            {
                ExpectedResources = preview.Resources.Select(x => new ResourceKey(x.Type, x.Provider, x.Id)).ToArray(),
                BatchId = "manual:date"
            };
            var executionResponse = await client.PostAsJsonAsync("api/v2/admin/collection/task-batches",
                ToPreviewSelection(execute));
            executionResponse.EnsureSuccessStatusCode();
            var executionEnvelope = await executionResponse.Content.ReadFromJsonAsync<CreateCollectionTaskBatchResponse>();
            var execution = executionEnvelope?.Submission;
            Assert.AreEqual<string?>("PreviewSelection", execution?.Mode);
            Assert.IsNotNull(execution?.PreviewSelection);
            Assert.HasCount(2, await collection.GetTasksAsync());

            var trainerRequest = new BulkCollectionOperationRequest("horse-profile", 1,
                CollectionReason.ManualRefresh, BulkCollectionSelection.HorsesByTrainer, TrainerId: "T1");
            var trainerPreviewEnvelope = await (await client.PostAsJsonAsync(
                "api/v2/admin/collection/task-batch-previews", ToPreview(trainerRequest))).Content
                .ReadFromJsonAsync<PreviewCollectionTaskBatchResponse>();
            var trainerPreview = trainerPreviewEnvelope?.Preview;
            Assert.IsNotNull(trainerPreview);
            Assert.AreEqual(1, trainerPreview.Resources.Count);
            Assert.AreEqual(CollectionResourceType.Horse, trainerPreview.Resources[0].Type);
            Assert.AreEqual("JRA", trainerPreview.Resources[0].Provider);
            Assert.AreEqual("H1", trainerPreview.Resources[0].Id);

            await collection.SuppressResourceAsync(new(CollectionResourceType.Horse, "JRA", "H1"),
                "Merged horse was deleted", "repair-1", DateTimeOffset.UtcNow);
            var suppressedDatePreview = await (await client.PostAsJsonAsync(
                "api/v2/admin/collection/task-batch-previews", ToPreview(dateRequest))).Content
                .ReadFromJsonAsync<PreviewCollectionTaskBatchResponse>();
            Assert.IsNotNull(suppressedDatePreview?.Preview);
            Assert.AreEqual(1, suppressedDatePreview.Preview.Resources.Count);
            Assert.AreEqual(CollectionResourceType.Horse, suppressedDatePreview.Preview.Resources[0].Type);
            Assert.AreEqual("JRA", suppressedDatePreview.Preview.Resources[0].Provider);
            Assert.AreEqual("H2", suppressedDatePreview.Preview.Resources[0].Id);
            var explicitRequest = new BulkCollectionOperationRequest("horse-profile", 1,
                CollectionReason.ManualRefresh, BulkCollectionSelection.SpecificResources,
                Resources: [new(CollectionResourceType.Horse, "JRA", "H1"), new(CollectionResourceType.Horse, "JRA", "H2")]);
            var explicitPreview = await (await client.PostAsJsonAsync(
                "api/v2/admin/collection/task-batch-previews", ToPreview(explicitRequest))).Content
                .ReadFromJsonAsync<PreviewCollectionTaskBatchResponse>();
            Assert.IsNotNull(explicitPreview?.Preview);
            Assert.AreEqual(1, explicitPreview.Preview.Resources.Count);
            Assert.AreEqual(CollectionResourceType.Horse, explicitPreview.Preview.Resources[0].Type);
            Assert.AreEqual("JRA", explicitPreview.Preview.Resources[0].Provider);
            Assert.AreEqual("H2", explicitPreview.Preview.Resources[0].Id);
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

    private static PreviewCollectionTaskBatchRequest ToPreview(BulkCollectionOperationRequest request)
        => new(new PreviewCollectionTaskBatchInputDto(request.DefinitionId, request.RequestedRevision,
            request.Reason, request.Selection, request.Provider,
            request.Resources?.Select(x => new CollectionResourceKeyDto(x.Type, x.Provider, x.Id)).ToArray(),
            request.From, request.To, request.TrainerId, request.LastCollectedBefore, request.ImpactRevision,
            request.ExpectedResources?.Select(x => new CollectionResourceKeyDto(x.Type, x.Provider, x.Id)).ToArray(),
            request.BatchId, request.Lane, request.Priority));

    private static CreateCollectionTaskBatchRequest ToPreviewSelection(BulkCollectionOperationRequest request)
        => new(new CreateCollectionTaskBatchInputDto("PreviewSelection",
            new CollectionSelectionTaskBatchInputDto(request.DefinitionId, request.RequestedRevision,
                request.Reason, request.Selection.ToString(), request.Provider,
                request.Resources?.Select(x => new CollectionResourceKeyDto(x.Type, x.Provider, x.Id)).ToArray(),
                request.From, request.To, request.TrainerId, request.LastCollectedBefore,
                request.ExpectedResources?.Select(x => new CollectionResourceKeyDto(x.Type, x.Provider, x.Id)).ToArray(),
                request.BatchId, request.Lane, request.Priority)));

    private static void Set<T>(RacePredictionContextReadModel model, string property, T value) =>
        typeof(RacePredictionContextReadModel).GetProperty(property)!.SetValue(model, value);
}
