using System.Text.Json;
using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Contracts.Tests;

[TestClass]
public sealed class CollectionRuntimeStatusSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void RuntimeResponse_PreservesWrapperActionOrderEnumsAndNullableFields()
    {
        var status = new CollectionRuntimeActionStatusDto(CollectionRuntimeAction.MetricDelivery,
            Enabled: true, EffectiveInterval: null, CollectionRuntimeState.Waiting,
            CollectionRuntimeReason.NoDueWork, LastStartedAtUtc: null,
            LastCompletedAtUtc: DateTimeOffset.Parse("2026-10-08T03:00:00Z"),
            LastSuccessfulCycleAtUtc: DateTimeOffset.Parse("2026-10-08T03:00:00Z"),
            LastProgressAtUtc: null, LastDurationMilliseconds: 0,
            InspectedCount: 0, CreatedCount: 0, ReclaimedCount: 0, SentCount: 0,
            CompletedCount: 0, ConsecutiveErrors: 0);
        var dispatcher = status with
        {
            Action = CollectionRuntimeAction.Dispatcher,
            EffectiveInterval = TimeSpan.FromSeconds(1),
            State = CollectionRuntimeState.NotObserved,
            Reason = null,
            LastCompletedAtUtc = null,
            LastSuccessfulCycleAtUtc = null,
        };
        var response = new GetCollectionRuntimeStatusResponse(new(Guid.Parse("d8d969b0-7d4a-4f93-9840-9b0ac90a4fa7"),
            DateTimeOffset.Parse("2026-10-08T02:00:00Z"), DateTimeOffset.Parse("2026-10-08T03:00:00Z"),
            [dispatcher, status]));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonOptions));
        var runtime = json.RootElement.GetProperty("runtime");
        Assert.AreEqual("d8d969b0-7d4a-4f93-9840-9b0ac90a4fa7", runtime.GetProperty("instanceId").GetString());
        Assert.AreEqual("2026-10-08T02:00:00+00:00", runtime.GetProperty("instanceStartedAtUtc").GetString());
        var action = runtime.GetProperty("actions")[1];
        Assert.AreEqual((int)CollectionRuntimeAction.MetricDelivery, action.GetProperty("action").GetInt32());
        Assert.AreEqual((int)CollectionRuntimeState.Waiting, action.GetProperty("state").GetInt32());
        Assert.AreEqual((int)CollectionRuntimeReason.NoDueWork, action.GetProperty("reason").GetInt32());
        Assert.AreEqual(JsonValueKind.Null, action.GetProperty("effectiveInterval").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, action.GetProperty("lastStartedAtUtc").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, action.GetProperty("lastProgressAtUtc").ValueKind);
        Assert.AreEqual("2026-10-08T03:00:00+00:00", action.GetProperty("lastCompletedAtUtc").GetString());
        var periodic = runtime.GetProperty("actions")[0];
        Assert.AreEqual((int)CollectionRuntimeAction.Dispatcher, periodic.GetProperty("action").GetInt32());
        Assert.AreEqual("00:00:01", periodic.GetProperty("effectiveInterval").GetString());
        Assert.AreEqual(JsonValueKind.Null, periodic.GetProperty("reason").ValueKind);

        var roundTrip = JsonSerializer.Deserialize<GetCollectionRuntimeStatusResponse>(json.RootElement.GetRawText(),
            JsonOptions);
        Assert.IsNotNull(roundTrip);
        Assert.AreEqual(CollectionRuntimeAction.MetricDelivery,
            roundTrip.Runtime.Actions.Single(x => x.Action == CollectionRuntimeAction.MetricDelivery).Action);
        Assert.AreEqual(TimeSpan.FromSeconds(1),
            roundTrip.Runtime.Actions.Single(x => x.Action == CollectionRuntimeAction.Dispatcher).EffectiveInterval);
        Assert.IsNull(roundTrip.Runtime.Actions.Single(x => x.Action == CollectionRuntimeAction.MetricDelivery).EffectiveInterval);
        Assert.IsNull(roundTrip.Runtime.Actions.Single(x => x.Action == CollectionRuntimeAction.MetricDelivery).LastProgressAtUtc);
    }
}
