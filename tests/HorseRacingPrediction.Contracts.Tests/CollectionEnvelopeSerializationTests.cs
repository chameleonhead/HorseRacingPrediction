using System.Text.Json;
using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Contracts.Tests;

[TestClass]
public sealed class CollectionEnvelopeSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void CollectionRequestsKeepTheFrozenBodyRootsAndInputValues()
    {
        var resource = new CollectionResourceTaskInputDto(CollectionResourceType.Horse, "JRA", "H001",
            "horse-profile", 3, CollectionReason.ManualRefresh, CollectionLane.Realtime, 70,
            "https://example.test/horses/H001", "batch-1", new DateOnly(2026, 9, 12),
            new Dictionary<string, string> { ["caller"] = "worker" });
        var requests = new (object Request, string Root)[]
        {
            (new CreateCollectionTaskRequest(new("Resource", resource)), "task"),
            (new PreviewRacePeriodRecollectionRequest(new(new(2026, 9, 1), new(2026, 9, 12), "JRA", "period-1")), "period"),
            (new AcquireNextExecutionRequest(new(new(Guid.NewGuid(), Guid.NewGuid(), "reservation"), "queue-1")), "acquisition"),
        };

        foreach (var (request, root) in requests)
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(request, JsonOptions));
            Assert.AreEqual(root, json.RootElement.EnumerateObject().Single().Name, request.GetType().Name);
        }

        using var task = JsonDocument.Parse(JsonSerializer.Serialize(requests[0].Request, JsonOptions));
        var taskResource = task.RootElement.GetProperty("task").GetProperty("resource");
        Assert.AreEqual("H001", taskResource.GetProperty("resourceId").GetString());
        Assert.AreEqual((int)CollectionReason.ManualRefresh, taskResource.GetProperty("reason").GetInt32());
        Assert.AreEqual((int)CollectionLane.Realtime, taskResource.GetProperty("lane").GetInt32());
        Assert.AreEqual("batch-1", taskResource.GetProperty("batchId").GetString());

        using var period = JsonDocument.Parse(JsonSerializer.Serialize(requests[1].Request, JsonOptions));
        Assert.AreEqual("2026-09-01", period.RootElement.GetProperty("period").GetProperty("from").GetString());
        Assert.AreEqual("period-1", period.RootElement.GetProperty("period").GetProperty("batchId").GetString());
    }

    [TestMethod]
    public void ExecutionResponsePreservesStartBeforeOffsetAndNull()
    {
        var startBefore = DateTimeOffset.Parse("2026-10-01T12:34:56.1234567-04:00");
        var response = new AcquireNextExecutionResponse(new(CollectionExecutionAcquireStatus.NoWork,
            NoWorkReason: CollectionExecutionNoWorkReason.PipelinePaused, StartBefore: startBefore));
        var json = JsonSerializer.Serialize(response, JsonOptions);

        using var document = JsonDocument.Parse(json);
        var acquisition = document.RootElement.GetProperty("acquisition");
        Assert.AreEqual("2026-10-01T12:34:56.1234567-04:00", acquisition.GetProperty("startBefore").GetString());
        var roundTrip = JsonSerializer.Deserialize<AcquireNextExecutionResponse>(json, JsonOptions);
        Assert.AreEqual(startBefore, roundTrip!.Acquisition.StartBefore);
        Assert.AreEqual(startBefore.Offset, roundTrip.Acquisition.StartBefore!.Value.Offset);

        var noStartBefore = JsonSerializer.Serialize(new AcquireNextExecutionResponse(
            new(CollectionExecutionAcquireStatus.NoWork)), JsonOptions);
        using var noStartBeforeJson = JsonDocument.Parse(noStartBefore);
        Assert.AreEqual(JsonValueKind.Null,
            noStartBeforeJson.RootElement.GetProperty("acquisition").GetProperty("startBefore").ValueKind);
    }

    [TestMethod]
    public void MonitoringResponseKeepsTheFormerReportAndOperationalFindingFields()
    {
        var finding = new CollectionOperationalFindingDto("fingerprint", "OperationalCondition",
            CollectionFindingClassificationDto.OperationalCondition, "Warning",
            DateTimeOffset.Parse("2026-10-01T09:00:00+09:00"), DateTimeOffset.Parse("2026-10-01T09:01:00+09:00"),
            "summary", ["evidence"], "restart-worker");
        var report = new CollectionMonitoringReportDto(finding.FirstObservedAt, finding.LastObservedAt,
            true, true, false, null, false, [finding], CollectionMonitoringOutcomeDto.FindingRecorded,
            [new("race-card", "Realtime", "JRA|race-card", 10, 8, 5, 2, 1.5, "Healthy")]);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            new GetCollectionMonitoringFindingsResponse(report), JsonOptions));
        var payload = json.RootElement.GetProperty("findings");
        Assert.AreEqual("2026-10-01T09:00:00+09:00", payload.GetProperty("cutoff").GetString());
        Assert.AreEqual((int)CollectionMonitoringOutcomeDto.FindingRecorded, payload.GetProperty("outcome").GetInt32());
        Assert.AreEqual("fingerprint", payload.GetProperty("findings")[0].GetProperty("fingerprint").GetString());
        Assert.AreEqual("restart-worker", payload.GetProperty("findings")[0].GetProperty("nextSafeOperation").GetString());
        Assert.AreEqual(10, payload.GetProperty("definitionFlows")[0].GetProperty("arrived").GetInt32());
    }
}
