using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Amazon.CloudWatch.Model;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionExecutionAcquireEndpointTests
{
    private const string AcquirePath = "/api/v2/internal/collection/execution-leases";

    [TestMethod]
    public async Task Acquire_AcquiredReturns201JsonAndNoWorkReturns200WithTypedReasonAndReleaseOutcome()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync(aggregationDelayMilliseconds: 0);
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = app.Services.GetRequiredService<CollectionPlatformStore>();
        var collectionOptions = app.Services.GetRequiredService<IOptions<CollectionPlatformOptions>>();
        var (wake, candidate, dbOptions) = await CreateReservedWakeAsync(store, collectionOptions, "acquired-then-paused");
        await store.SetPausedAsync(true, "contract test", DateTimeOffset.UtcNow);
        using var noWorkResponse = await http.PostAsJsonAsync(AcquirePath,
            new CollectionExecutionAcquireRequest(wake, "queue-no-work"));
        Assert.AreEqual(HttpStatusCode.OK, noWorkResponse.StatusCode);
        using var noWorkJson = JsonDocument.Parse(await noWorkResponse.Content.ReadAsStringAsync());
        Assert.AreEqual("noWork", noWorkJson.RootElement.GetProperty("status").GetString());
        Assert.AreEqual("pipelinePaused", noWorkJson.RootElement.GetProperty("noWorkReason").GetString());
        Assert.IsTrue(noWorkJson.RootElement.GetProperty("safeToReleaseReservation").GetBoolean());
        Assert.AreEqual("released", noWorkJson.RootElement.GetProperty("reservationReleaseOutcome").GetString());
        await using var verify = new CollectionPlatformDbContext(dbOptions);
        var released = await verify.DispatchOutbox.SingleAsync(x => x.OutboxId == candidate.OutboxId);
        Assert.IsNull(released.ReservationToken);
        Assert.IsNull(released.WakeId);

        await store.SetPausedAsync(false, null, DateTimeOffset.UtcNow);
        var queue = new CapturingQueue();
        var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue,
            Options.Create(new CollectionQueueOptions
            {
                Enabled = true,
                DispatchBatchSize = 1,
                EnvelopeMaxTasks = 1,
                MaxInFlightEnvelopes = 1,
                AggregationDelayMilliseconds = 0,
                OutboxReservationSeconds = 60
            }), NullLogger<CollectionPlatformOutboxDispatcher>.Instance);
        await dispatcher.DispatchOnceAsync(CancellationToken.None);
        var rewake = queue.Wakes.Single();
        Assert.AreNotEqual(wake.WakeId, rewake.WakeId);
        using var acquiredResponse = await http.PostAsJsonAsync(AcquirePath,
            new CollectionExecutionAcquireRequest(rewake, "queue-acquired"));
        Assert.AreEqual(HttpStatusCode.Created, acquiredResponse.StatusCode);
        using var acquiredJson = JsonDocument.Parse(await acquiredResponse.Content.ReadAsStringAsync());
        Assert.AreEqual("acquired", acquiredJson.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(acquiredJson.RootElement.GetProperty("safeToReleaseReservation").GetBoolean());
        Assert.IsFalse(acquiredJson.RootElement.TryGetProperty("noWorkReason", out _));
    }

    [TestMethod]
    public async Task AcquireEndpoint_ReturnsTypedNoWorkWhenTelemetryQueueIsFull()
    {
        var telemetry = new CollectionDispatchTelemetry(new RejectingMetricQueue(), Options.Create(new CollectionQueueOptions
        {
            Enabled = true,
            Provider = "Sqs",
            TelemetryDefinitionLabels = ["race-detail"],
        }));
        var (app, client) = await TestApplicationFactory.CreateAsync(aggregationDelayMilliseconds: 0,
            dispatchTelemetry: telemetry);
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);

        using var response = await http.PostAsJsonAsync(AcquirePath,
            new CollectionExecutionAcquireRequest(new CollectionWakeSignal(Guid.Empty, Guid.Empty, string.Empty), "opaque"));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("noWork", json.RootElement.GetProperty("status").GetString());
        Assert.AreEqual("invalidRequest", json.RootElement.GetProperty("noWorkReason").GetString());
    }

    [TestMethod]
    public async Task Acquire_TokenMismatchAndActiveLeaseNoWorkDoNotReleaseReservation()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync(aggregationDelayMilliseconds: 0);
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = app.Services.GetRequiredService<CollectionPlatformStore>();
        var collectionOptions = app.Services.GetRequiredService<IOptions<CollectionPlatformOptions>>();

        var (mismatchWake, mismatchCandidate, dbOptions) = await CreateReservedWakeAsync(store, collectionOptions,
            "token-mismatch");
        using (var response = await http.PostAsJsonAsync(AcquirePath,
                   new CollectionExecutionAcquireRequest(mismatchWake with { ReservationToken = "wrong-token" }, "bad-token")))
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.AreEqual("reservationUnavailable", json.RootElement.GetProperty("noWorkReason").GetString());
            Assert.IsFalse(json.RootElement.GetProperty("safeToReleaseReservation").GetBoolean());
            Assert.IsFalse(json.RootElement.TryGetProperty("reservationReleaseOutcome", out _));
        }

        var activeWake = mismatchWake;
        var activeCandidate = mismatchCandidate;
        await using (var db = new CollectionPlatformDbContext(dbOptions))
        {
            db.ExecutionLeases.Add(new CollectionExecutionLeaseEntity
            {
                ExecutionBatchId = Guid.NewGuid(),
                DispatchEnvelopeId = activeWake.DispatchEnvelopeId,
                WakeId = Guid.NewGuid(),
                ReservationToken = "different-active-token",
                LeaseToken = Guid.NewGuid().ToString("N"),
                Status = "Running",
                LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1),
                CreatedAt = DateTimeOffset.UtcNow,
                QueueMessageId = "active-lease"
            });
            await db.SaveChangesAsync();
        }

        using (var response = await http.PostAsJsonAsync(AcquirePath,
                   new CollectionExecutionAcquireRequest(activeWake, "active-lease-wake")))
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.AreEqual("leaseConflict", json.RootElement.GetProperty("noWorkReason").GetString());
            Assert.IsFalse(json.RootElement.GetProperty("safeToReleaseReservation").GetBoolean());
            Assert.IsFalse(json.RootElement.TryGetProperty("reservationReleaseOutcome", out _));
        }

        await using var verify = new CollectionPlatformDbContext(dbOptions);
        Assert.AreEqual(mismatchWake.ReservationToken,
            (await verify.DispatchOutbox.SingleAsync(x => x.OutboxId == mismatchCandidate.OutboxId)).ReservationToken);
        Assert.AreEqual(activeWake.ReservationToken,
            (await verify.DispatchOutbox.SingleAsync(x => x.OutboxId == activeCandidate.OutboxId)).ReservationToken);
    }

    [TestMethod]
    public async Task ExpiredWakeIsTypedNoWorkThenDispatcherCreatesFreshWakeThatAcquires()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync(aggregationDelayMilliseconds: 0);
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = app.Services.GetRequiredService<CollectionPlatformStore>();
        var collectionOptions = app.Services.GetRequiredService<IOptions<CollectionPlatformOptions>>();
        var (expiredWake, candidate, dbOptions) = await CreateReservedWakeAsync(store, collectionOptions,
            "expired-rewake", TimeSpan.FromSeconds(1));

        using (var response = await http.PostAsJsonAsync(AcquirePath,
                   new CollectionExecutionAcquireRequest(expiredWake, "expired-message")))
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.AreEqual("reservationUnavailable", json.RootElement.GetProperty("noWorkReason").GetString());
            Assert.IsFalse(json.RootElement.GetProperty("safeToReleaseReservation").GetBoolean());
        }

        var queue = new CapturingQueue();
        var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue,
            Options.Create(new CollectionQueueOptions
            {
                Enabled = true,
                DispatchBatchSize = 1,
                EnvelopeMaxTasks = 1,
                MaxInFlightEnvelopes = 1,
                AggregationDelayMilliseconds = 0,
                OutboxReservationSeconds = 60
            }), NullLogger<CollectionPlatformOutboxDispatcher>.Instance);
        await dispatcher.DispatchOnceAsync(CancellationToken.None);
        var freshWake = queue.Wakes.Single();
        Assert.AreNotEqual(expiredWake.WakeId, freshWake.WakeId);
        Assert.IsTrue((await store.GetTasksAsync()).Any(x => x.TaskId == candidate.Notification.TaskId));

        using var freshResponse = await http.PostAsJsonAsync(AcquirePath,
            new CollectionExecutionAcquireRequest(freshWake, "fresh-message"));
        Assert.AreEqual(HttpStatusCode.Created, freshResponse.StatusCode);
        using var freshJson = JsonDocument.Parse(await freshResponse.Content.ReadAsStringAsync());
        Assert.AreEqual("acquired", freshJson.RootElement.GetProperty("status").GetString());
        await using var verify = new CollectionPlatformDbContext(dbOptions);
        Assert.IsTrue(await verify.ExecutionLeases.AnyAsync(x => x.DispatchEnvelopeId == freshWake.DispatchEnvelopeId));
    }

    [TestMethod]
    public async Task DelayedOldWakeCannotReleaseReplacementReservationOrPreventItsAcquire()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync(aggregationDelayMilliseconds: 0);
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = app.Services.GetRequiredService<CollectionPlatformStore>();
        var collectionOptions = app.Services.GetRequiredService<IOptions<CollectionPlatformOptions>>();
        var (oldWake, candidate, dbOptions) = await CreateReservedWakeAsync(store, collectionOptions,
            "delayed-old-wake");

        await using (var expire = new CollectionPlatformDbContext(dbOptions))
        {
            var oldReservation = await expire.DispatchOutbox.SingleAsync(x => x.OutboxId == candidate.OutboxId);
            oldReservation.ReservedUntilUnixMilliseconds = DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeMilliseconds();
            await expire.SaveChangesAsync();
        }

        var queue = new CapturingQueue();
        var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue,
            Options.Create(new CollectionQueueOptions
            {
                Enabled = true,
                DispatchBatchSize = 1,
                EnvelopeMaxTasks = 1,
                MaxInFlightEnvelopes = 1,
                AggregationDelayMilliseconds = 0,
                OutboxReservationSeconds = 60
            }), NullLogger<CollectionPlatformOutboxDispatcher>.Instance);
        await dispatcher.DispatchOnceAsync(CancellationToken.None);
        var replacementWake = queue.Wakes.Single();
        Assert.AreNotEqual(oldWake.WakeId, replacementWake.WakeId);
        Assert.AreNotEqual(oldWake.DispatchEnvelopeId, replacementWake.DispatchEnvelopeId);
        Assert.AreNotEqual(oldWake.ReservationToken, replacementWake.ReservationToken);

        using (var delayedResponse = await http.PostAsJsonAsync(AcquirePath,
                   new CollectionExecutionAcquireRequest(oldWake, "delayed-old-message")))
        {
            Assert.AreEqual(HttpStatusCode.OK, delayedResponse.StatusCode);
            using var delayedJson = JsonDocument.Parse(await delayedResponse.Content.ReadAsStringAsync());
            Assert.AreEqual("noWork", delayedJson.RootElement.GetProperty("status").GetString());
            Assert.AreEqual("reservationUnavailable", delayedJson.RootElement.GetProperty("noWorkReason").GetString());
            Assert.IsFalse(delayedJson.RootElement.GetProperty("safeToReleaseReservation").GetBoolean());
            Assert.IsFalse(delayedJson.RootElement.TryGetProperty("reservationReleaseOutcome", out _));
        }

        await using (var verifyReplacement = new CollectionPlatformDbContext(dbOptions))
        {
            var replacement = await verifyReplacement.DispatchOutbox.SingleAsync(x => x.OutboxId == candidate.OutboxId);
            Assert.AreEqual(replacementWake.WakeId, replacement.WakeId);
            Assert.AreEqual(replacementWake.DispatchEnvelopeId, replacement.EnvelopeId);
            Assert.AreEqual(replacementWake.ReservationToken, replacement.ReservationToken);
            Assert.IsNull(replacement.DispatchedAt);
        }

        using var replacementResponse = await http.PostAsJsonAsync(AcquirePath,
            new CollectionExecutionAcquireRequest(replacementWake, "replacement-message"));
        Assert.AreEqual(HttpStatusCode.Created, replacementResponse.StatusCode);
        using var replacementJson = JsonDocument.Parse(await replacementResponse.Content.ReadAsStringAsync());
        Assert.AreEqual("acquired", replacementJson.RootElement.GetProperty("status").GetString());
        await using var verifyLease = new CollectionPlatformDbContext(dbOptions);
        Assert.IsTrue(await verifyLease.ExecutionLeases.AnyAsync(x => x.DispatchEnvelopeId == replacementWake.DispatchEnvelopeId));
    }

    private static async Task<(CollectionWakeSignal Wake, PendingCollectionDispatch Candidate,
        DbContextOptions<CollectionPlatformDbContext> DbOptions)> CreateReservedWakeAsync(
        CollectionPlatformStore store, IOptions<CollectionPlatformOptions> collectionOptions, string id,
        TimeSpan? reservationDuration = null)
    {
        var requestedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var definition = new CollectionDefinitionId("race-detail");
        await store.RegisterDefinitionAsync(definition, "Race detail", CollectionResourceType.Race, 1,
            "initial", false);
        var receipt = await store.RequestAsync(new(CollectionResourceType.Race, "JRA", $"ACQUIRE-{id}"),
            definition, 1, CollectionReason.Initial, requestedAt, CollectionLane.Background, 10);
        var candidate = (await store.GetPendingDispatchesAsync(DateTimeOffset.UtcNow, 10)).Single(x =>
            x.Notification.TaskId == receipt.TaskId);
        var wakeId = Guid.NewGuid();
        var envelopeId = Guid.NewGuid();
        var reservationToken = $"token-{id}";
        var duration = reservationDuration ?? TimeSpan.FromMinutes(2);
        var reserveAt = DateTimeOffset.UtcNow.Add(duration < TimeSpan.FromSeconds(5)
            ? duration.Negate().Subtract(TimeSpan.FromSeconds(2)) : TimeSpan.Zero);
        Assert.IsTrue(await store.TryReserveDispatchesWithinCapacityAsync([candidate.OutboxId], reservationToken,
            envelopeId, wakeId, reserveAt, duration, 1));
        var options = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
            .UseSqlite($"Data Source={Path.Combine(collectionOptions.Value.StateDirectory, collectionOptions.Value.DatabaseFileName)};Pooling=False;Default Timeout=30")
            .Options;
        return (new(wakeId, envelopeId, reservationToken), candidate, options);
    }

    private sealed class CapturingQueue : ICollectionPlatformTaskQueue
    {
        public List<CollectionWakeSignal> Wakes { get; } = [];
        public Task<CollectionQueueSendReceipt> SendAsync(CollectionDispatchEnvelope envelope,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionQueueSendReceipt> SendWakeAsync(CollectionWakeSignal wake,
            CancellationToken cancellationToken)
        {
            Wakes.Add(wake);
            return Task.FromResult(new CollectionQueueSendReceipt($"message-{Wakes.Count}"));
        }
    }

    private sealed class RejectingMetricQueue : ICollectionDispatchMetricQueue
    {
        public bool TryEnqueueMetrics(IReadOnlyCollection<MetricDatum> metrics) => false;
        public bool TryEnqueueSnapshot(Func<CancellationToken, Task<IReadOnlyCollection<MetricDatum>>> snapshot) => false;
    }
}
