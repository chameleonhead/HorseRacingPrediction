using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using Amazon.CloudWatch.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionCompletionTransportTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task WorkerCompletion_ThroughHttp_PersistsIndependentArtifactsAndEvidence(bool cardUnavailable,
        bool ownerValidationFails)
    {
        await WithApiAsync(async (store, client) =>
        {
            var now = DateTimeOffset.UtcNow;
            var resource = new ResourceKey(CollectionResourceType.Race, "JRA", "20260920:Nakayama:3");
            var definition = new CollectionDefinitionId("race-detail");
            await store.RegisterDefinitionAsync(definition, "Race detail", CollectionResourceType.Race, 2, "facets", false);
            var receipt = await store.RequestAsync(resource, definition, 2, CollectionReason.Initial, now);
            var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
            var evidence = new RaceSchedulingEvidence(now.AddHours(-2), "JRA-RaceCard", now);
            var completion = new CollectionAttemptCompletion(ownerValidationFails
                    ? CollectionAttemptResult.ValidationFailure : CollectionAttemptResult.Succeeded,
                FailureImpact: CollectionFailureImpact.Isolated,
                StageOutcomes:
                [
                    cardUnavailable
                        ? new("ResolveCard", RaceArtifactKind.Card, CollectionAttemptResult.NotApplicable,
                            "OfficialRaceCardOutsideLookupPeriod")
                        : new("PersistCard", RaceArtifactKind.Card, CollectionAttemptResult.Succeeded, Persisted: true),
                    .. (cardUnavailable ? Array.Empty<CollectionStageOutcome>() :
                        [new("ValidateCardOwners", RaceArtifactKind.Card, ownerValidationFails
                            ? CollectionAttemptResult.ValidationFailure : CollectionAttemptResult.Succeeded,
                            ownerValidationFails ? "RaceCardOwnerIncomplete" : null, Persisted: !ownerValidationFails)]),
                    new("PersistResult", RaceArtifactKind.Result, CollectionAttemptResult.Succeeded,
                        RequestedUrl: new("https://www.jra.go.jp/JRADB/accessS.html"), Persisted: true),
                ], RaceEvidence: evidence);
            var worker = new CollectionPlatformWorkerClient(client,
                new CollectionDefinitionHandlerRegistry([new EvidenceHandler(completion)]));

            await worker.ExecuteAsync(new(taskId, 1), CancellationToken.None);

            var detail = await client.GetFromJsonAsync<CollectionResourceDetail>(
                "api/v2/admin/collection/resources/Race/JRA/20260920%3ANakayama%3A3/definitions/race-detail");
            Assert.IsNotNull(detail);
            Assert.AreEqual(ownerValidationFails ? CollectionTaskStatus.Failed : CollectionTaskStatus.Succeeded,
                detail.LatestTask!.Status);
            Assert.IsNotNull(detail.RaceArtifacts);
            Assert.HasCount(2, detail.RaceArtifacts);
            var card = detail.RaceArtifacts.Single(x => x.Artifact == RaceArtifactKind.Card);
            Assert.AreEqual(cardUnavailable ? RaceArtifactStatus.Unavailable : ownerValidationFails
                ? RaceArtifactStatus.Blocked : RaceArtifactStatus.Current, card.Status);
            Assert.AreEqual(cardUnavailable ? 0 : 2, card.AppliedRevision);
            Assert.AreEqual(cardUnavailable, card.LastPersistedAt is null);
            var result = detail.RaceArtifacts.Single(x => x.Artifact == RaceArtifactKind.Result);
            Assert.AreEqual(RaceArtifactStatus.Current, result.Status);
            Assert.AreEqual(2, result.AppliedRevision);
            Assert.IsNotNull(result.LastPersistedAt);
            Assert.AreEqual(evidence, detail.RaceEvidence);
            Assert.IsNotNull(detail.StageOutcomes);
            Assert.HasCount(cardUnavailable ? 2 : 3, detail.StageOutcomes);
            Assert.IsTrue(detail.StageOutcomes.All(x => x.AttemptId == detail.Attempts.Single().AttemptId));
            Assert.IsTrue(detail.StageOutcomes.Single(x => x.Artifact == RaceArtifactKind.Result).Persisted);
            Assert.IsFalse((await store.GetPipelineStateAsync()).IsPaused);
        });
    }

    [TestMethod]
    public async Task LegacyCompletionPayload_WithoutEvidence_RemainsCompatibleWithoutInventingFacets()
    {
        await WithApiAsync(async (store, client) =>
        {
            var now = DateTimeOffset.UtcNow;
            var resource = new ResourceKey(CollectionResourceType.Race, "JRA", "20260920:Nakayama:3");
            var definition = new CollectionDefinitionId("race-detail");
            await store.RegisterDefinitionAsync(definition, "Race detail", CollectionResourceType.Race, 2, "facets", false);
            var receipt = await store.RequestAsync(resource, definition, 2, CollectionReason.Initial, now);
            var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
            var lease = await store.AcquireAsync(taskId, 1, now, TimeSpan.FromMinutes(5));
            using var response = await client.PostAsJsonAsync($"api/v2/internal/collection/tasks/{taskId}/attempts",
                new { lease!.LeaseToken, Result = CollectionAttemptResult.Succeeded });
            response.EnsureSuccessStatusCode();
            var detail = await store.GetResourceDetailAsync(resource, definition);
            Assert.AreEqual(CollectionTaskStatus.Succeeded, detail!.LatestTask!.Status);
            Assert.IsEmpty(detail.RaceArtifacts!);
            Assert.IsEmpty(detail.StageOutcomes!);
            Assert.IsNull(detail.RaceEvidence);
        });
    }

    [TestMethod]
    public async Task CompletionEndpoint_CommitsWhenTelemetryQueueRejectsCompletionLookup()
    {
        var telemetry = new CollectionDispatchTelemetry(new RejectingMetricQueue(), Options.Create(new CollectionQueueOptions
        {
            Enabled = true,
            Provider = "Sqs",
            TelemetryDefinitionLabels = ["race-detail"],
        }));
        await WithApiAsync(async (store, client) =>
        {
            var resource = new ResourceKey(CollectionResourceType.Race, "JRA", "telemetry-rejected");
            var definition = new CollectionDefinitionId("race-detail");
            await store.RegisterDefinitionAsync(definition, "Race detail", CollectionResourceType.Race, 1, "initial", false);
            var receipt = await store.RequestAsync(resource, definition, 1, CollectionReason.Initial, DateTimeOffset.UtcNow);
            var taskId = receipt.TaskId!.Value;
            var lease = await store.AcquireAsync(taskId, 1, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

            using var response = await client.PostAsJsonAsync($"api/v2/internal/collection/tasks/{taskId}/attempts",
                new { lease!.LeaseToken, Result = CollectionAttemptResult.Succeeded });

            Assert.AreEqual(System.Net.HttpStatusCode.NoContent, response.StatusCode);
            Assert.AreEqual(CollectionTaskStatus.Succeeded,
                (await store.GetResourceDetailAsync(resource, definition))!.LatestTask!.Status);
        }, telemetry);
    }

    private static async Task WithApiAsync(Func<CollectionPlatformStore, HttpClient, Task> test,
        ICollectionDispatchTelemetry? telemetry = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-completion-transport", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions { StateDirectory = directory }));
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(store);
            builder.Services.AddSingleton<ICollectionDispatchTelemetry>(telemetry ?? new NullCollectionDispatchTelemetry());
            await using var app = builder.Build();
            app.MapCollectionApiV2Endpoints();
            await app.StartAsync();
            using var client = app.GetTestClient();
            await test(store, client);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class EvidenceHandler(CollectionAttemptCompletion completion) : ICollectionDefinitionHandler
    {
        public CollectionDefinitionId DefinitionId => new("race-detail");
        public CollectionResourceType ResourceType => CollectionResourceType.Race;
        public Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task, CancellationToken token)
            => Task.FromResult(completion);
    }

    private sealed class RejectingMetricQueue : ICollectionDispatchMetricQueue
    {
        public bool TryEnqueueMetrics(IReadOnlyCollection<MetricDatum> metrics) => false;
        public bool TryEnqueueSnapshot(Func<CancellationToken, Task<IReadOnlyCollection<MetricDatum>>> snapshot) => false;
    }
}
