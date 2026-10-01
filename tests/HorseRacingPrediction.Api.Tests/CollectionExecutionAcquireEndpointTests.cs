using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Amazon.CloudWatch.Model;

using HorseRacingPrediction.Contracts.Collection;

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
        using var noWorkResponse = await PostAcquireAsync(http,
            new CollectionExecutionAcquireRequest(wake, "queue-no-work"));
        Assert.AreEqual(HttpStatusCode.OK, noWorkResponse.StatusCode);
        using var noWorkJson = JsonDocument.Parse(await noWorkResponse.Content.ReadAsStringAsync());
        Assert.AreEqual("noWork", Acquisition(noWorkJson).GetProperty("status").GetString());
        Assert.AreEqual("pipelinePaused", Acquisition(noWorkJson).GetProperty("noWorkReason").GetString());
        Assert.IsTrue(Acquisition(noWorkJson).GetProperty("safeToReleaseReservation").GetBoolean());
        Assert.AreEqual("released", Acquisition(noWorkJson).GetProperty("reservationReleaseOutcome").GetString());
        Assert.IsFalse(Acquisition(noWorkJson).TryGetProperty("startBefore", out _));
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
        using var acquiredResponse = await PostAcquireAsync(http,
            new CollectionExecutionAcquireRequest(rewake, "queue-acquired"));
        Assert.AreEqual(HttpStatusCode.Created, acquiredResponse.StatusCode);
        using var acquiredJson = JsonDocument.Parse(await acquiredResponse.Content.ReadAsStringAsync());
        Assert.AreEqual("acquired", Acquisition(acquiredJson).GetProperty("status").GetString());
        Assert.IsFalse(Acquisition(acquiredJson).GetProperty("safeToReleaseReservation").GetBoolean());
        Assert.IsFalse(Acquisition(acquiredJson).TryGetProperty("noWorkReason", out _));
        var actualStartBefore = DateTimeOffset.Parse(Acquisition(acquiredJson).GetProperty("startBefore").GetString()!);
        Assert.AreEqual(TimeSpan.FromHours(9), actualStartBefore.Offset);
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

        using var response = await PostAcquireAsync(http,
            new CollectionExecutionAcquireRequest(new CollectionWakeSignal(Guid.Empty, Guid.Empty, string.Empty), "opaque"));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("noWork", Acquisition(json).GetProperty("status").GetString());
        Assert.AreEqual("invalidRequest", Acquisition(json).GetProperty("noWorkReason").GetString());
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
        using (var response = await PostAcquireAsync(http,
                   new CollectionExecutionAcquireRequest(mismatchWake with { ReservationToken = "wrong-token" }, "bad-token")))
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.AreEqual("reservationUnavailable", Acquisition(json).GetProperty("noWorkReason").GetString());
            Assert.IsFalse(Acquisition(json).GetProperty("safeToReleaseReservation").GetBoolean());
            Assert.IsFalse(Acquisition(json).TryGetProperty("reservationReleaseOutcome", out _));
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

        using (var response = await PostAcquireAsync(http,
                   new CollectionExecutionAcquireRequest(activeWake, "active-lease-wake")))
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.AreEqual("leaseConflict", Acquisition(json).GetProperty("noWorkReason").GetString());
            Assert.IsFalse(Acquisition(json).GetProperty("safeToReleaseReservation").GetBoolean());
            Assert.IsFalse(Acquisition(json).TryGetProperty("reservationReleaseOutcome", out _));
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

        using (var response = await PostAcquireAsync(http,
                   new CollectionExecutionAcquireRequest(expiredWake, "expired-message")))
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.AreEqual("reservationUnavailable", Acquisition(json).GetProperty("noWorkReason").GetString());
            Assert.IsFalse(Acquisition(json).GetProperty("safeToReleaseReservation").GetBoolean());
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

        using var freshResponse = await PostAcquireAsync(http,
            new CollectionExecutionAcquireRequest(freshWake, "fresh-message"));
        Assert.AreEqual(HttpStatusCode.Created, freshResponse.StatusCode);
        using var freshJson = JsonDocument.Parse(await freshResponse.Content.ReadAsStringAsync());
        Assert.AreEqual("acquired", Acquisition(freshJson).GetProperty("status").GetString());
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

        using (var delayedResponse = await PostAcquireAsync(http,
                   new CollectionExecutionAcquireRequest(oldWake, "delayed-old-message")))
        {
            Assert.AreEqual(HttpStatusCode.OK, delayedResponse.StatusCode);
            using var delayedJson = JsonDocument.Parse(await delayedResponse.Content.ReadAsStringAsync());
            Assert.AreEqual("noWork", Acquisition(delayedJson).GetProperty("status").GetString());
            Assert.AreEqual("reservationUnavailable", Acquisition(delayedJson).GetProperty("noWorkReason").GetString());
            Assert.IsFalse(Acquisition(delayedJson).GetProperty("safeToReleaseReservation").GetBoolean());
            Assert.IsFalse(Acquisition(delayedJson).TryGetProperty("reservationReleaseOutcome", out _));
        }

        await using (var verifyReplacement = new CollectionPlatformDbContext(dbOptions))
        {
            var replacement = await verifyReplacement.DispatchOutbox.SingleAsync(x => x.OutboxId == candidate.OutboxId);
            Assert.AreEqual(replacementWake.WakeId, replacement.WakeId);
            Assert.AreEqual(replacementWake.DispatchEnvelopeId, replacement.EnvelopeId);
            Assert.AreEqual(replacementWake.ReservationToken, replacement.ReservationToken);
            Assert.IsNull(replacement.DispatchedAt);
        }

        using var replacementResponse = await PostAcquireAsync(http,
            new CollectionExecutionAcquireRequest(replacementWake, "replacement-message"));
        Assert.AreEqual(HttpStatusCode.Created, replacementResponse.StatusCode);
        using var replacementJson = JsonDocument.Parse(await replacementResponse.Content.ReadAsStringAsync());
        Assert.AreEqual("acquired", Acquisition(replacementJson).GetProperty("status").GetString());
        await using var verifyLease = new CollectionPlatformDbContext(dbOptions);
        Assert.IsTrue(await verifyLease.ExecutionLeases.AnyAsync(x => x.DispatchEnvelopeId == replacementWake.DispatchEnvelopeId));
    }

    [TestMethod]
    public void AcquireResponseContract_RoundTripsNonJstStartBeforeOffset()
    {
        var startBefore = DateTimeOffset.Parse("2026-10-01T12:34:56.1234567-04:00", CultureInfo.InvariantCulture);
        var contract = new AcquireNextExecutionResponse(new CollectionExecutionAcquireResultDto(
            CollectionExecutionAcquireStatus.Acquired, StartBefore: startBefore));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var json = JsonSerializer.Serialize(contract, options);
        using var parsed = JsonDocument.Parse(json);
        var wireValue = parsed.RootElement.GetProperty("acquisition").GetProperty("startBefore").GetString();
        var roundTripped = JsonSerializer.Deserialize<AcquireNextExecutionResponse>(json, options);

        Assert.AreEqual(startBefore.ToString("O", CultureInfo.InvariantCulture), wireValue);
        Assert.IsNotNull(roundTripped?.Acquisition.StartBefore);
        Assert.AreEqual(TimeSpan.FromHours(-4), roundTripped.Acquisition.StartBefore.Value.Offset);
        Assert.AreEqual(startBefore, roundTripped.Acquisition.StartBefore.Value);
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

    private static Task<HttpResponseMessage> PostAcquireAsync(HttpClient client,
        CollectionExecutionAcquireRequest request)
    {
        var wake = new CollectionWakeSignalDto(request.Wake.WakeId, request.Wake.DispatchEnvelopeId,
            request.Wake.ReservationToken, request.Wake.ContractVersion);
        return client.PostAsJsonAsync(AcquirePath,
            new AcquireNextExecutionRequest(new AcquireNextExecutionInputDto(wake, request.QueueMessageId)));
    }

    private static JsonElement Acquisition(JsonDocument response)
        => response.RootElement.GetProperty("acquisition");

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
