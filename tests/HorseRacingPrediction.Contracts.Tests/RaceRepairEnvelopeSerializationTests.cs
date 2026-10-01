using System.Text.Json;

using HorseRacingPrediction.Contracts.Races;

namespace HorseRacingPrediction.Contracts.Tests;

[TestClass]
public sealed class RaceRepairEnvelopeSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void PathOnlyOperationRequestsDoNotSerializeTheirRouteRaceId()
    {
        var requests = new object[]
        {
            new GetRaceEntryRepairHoldRequest("route-race"),
            new GetRaceAssignmentFenceRequest("route-race"),
            new GetRaceEntryRepairRequest("route-race"),
            new ListRaceOddsSnapshotsRequest("route-race"),
            new GetPredictionComparisonRequest("route-race"),
        };

        foreach (var request in requests)
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(request, JsonOptions));
            Assert.AreEqual(0, json.RootElement.EnumerateObject().Count(), request.GetType().Name);
        }
    }

    [TestMethod]
    public void RepairAndOddsRequestsSerializeOnlyTheirFrozenNestedPayloads()
    {
        var manifest = new RaceEntryRepairManifestInputDto(3, "https://www.jra.go.jp/JRADB/accessD.html?CNAME=x",
            DateTimeOffset.Parse("2026-10-01T09:00:00+09:00"), "G1",
            [new("https://www.jra.go.jp/JRADB/accessD.html?CNAME=horse", 1, 1, "Owner")], new string('a', 64));
        var requests = new (object Request, string Payload, int PropertyCount)[]
        {
            (new PreviewRaceEntryRepairRequest("route-race", manifest), "manifest", 1),
            (new ApplyRaceEntryRepairRequest("route-race", "operation", "fingerprint", manifest), "manifest", 3),
            (new UpdateRaceEntryRepairHoldRequest("route-race", new("operation", 2, "reason")), "hold", 1),
            (new ReleaseRaceEntryRepairHoldRequest("route-race", new("operation", "hold", 2, 3,
                "assignment", CancelRepair: true)), "release", 1),
            (new CreateRaceOddsSnapshotRequest("route-race", new(DateTimeOffset.Parse("2026-10-01T09:00:00+09:00"),
                [new(1, 2.5m, 1)], [new("Win", "1", 2.5m, 1)])), "snapshot", 1)
        };

        foreach (var (request, payload, propertyCount) in requests)
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(request, JsonOptions));
            Assert.AreEqual(propertyCount, json.RootElement.EnumerateObject().Count(), request.GetType().Name);
            Assert.IsTrue(json.RootElement.TryGetProperty(payload, out _), request.GetType().Name);
            Assert.IsFalse(json.RootElement.TryGetProperty("raceId", out _), request.GetType().Name);
        }
    }

    [TestMethod]
    public void RepairOddsAndComparisonResponsesKeepNamedNestedDataAndPublicProjections()
    {
        var hold = new RaceRepairHoldDto("race-1", "hold-1", 2, "reason",
            DateTimeOffset.Parse("2026-10-01T09:00:00+09:00"), null, "fingerprint",
            1, 0, [], 4, 0, [], ["race-detail"]);
        var responses = new (object Response, string Property)[]
        {
            (new GetRaceEntryRepairHoldResponse(null), "hold"),
            (new UpdateRaceEntryRepairHoldResponse(hold), "hold"),
            (new ReleaseRaceEntryRepairHoldResponse(hold), "hold"),
            (new GetRaceAssignmentFenceResponse(new(2, "fingerprint")), "fence"),
            (new ListRaceOddsSnapshotsResponse([new(DateTimeOffset.Parse("2026-10-01T09:00:00+09:00"),
                [new(1, 2.5m, 1)], [new("Win", "1", 2.5m, 1)], [new(1, "horse-1", "entry-1", 1)])]), "oddsSnapshots"),
            (new GetPredictionComparisonResponse(new("race-1", "Race", "Horse", null,
                [new("ticket-1", "AI", "agent", HorseRacingPrediction.Contracts.Predictions.TicketStatus.Finalized,
                    0.8m, null, DateTimeOffset.Parse("2026-10-01T09:00:00+09:00"),
                    [new("entry-1", "◎", 1, 0.8m, null)],
                    new(DateTimeOffset.Parse("2026-10-01T09:01:00+09:00"), 2, ["Win"], 1.2m, 200m, 1.2m),
                    HorseRacingPrediction.Contracts.Predictions.EvaluationStatus.Ready)], [], null)), "comparison")
        };

        foreach (var (response, property) in responses)
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonOptions));
            Assert.IsTrue(json.RootElement.TryGetProperty(property, out _), response.GetType().Name);
        }

        using var comparison = JsonDocument.Parse(JsonSerializer.Serialize(responses[^1].Response, JsonOptions));
        var comparisonDto = comparison.RootElement.GetProperty("comparison");
        var ticket = comparisonDto.GetProperty("predictionTickets")[0];
        Assert.AreEqual("ticket-1", ticket.GetProperty("predictionTicketId").GetString());
        Assert.AreEqual(2, ticket.GetProperty("latestEvaluation").GetProperty("evaluationRevision").GetInt32());
        Assert.IsFalse(ticket.TryGetProperty("evaluations", out _));
        Assert.AreEqual((int)HorseRacingPrediction.Contracts.Predictions.TicketStatus.Finalized,
            ticket.GetProperty("status").GetInt32());
    }
}
