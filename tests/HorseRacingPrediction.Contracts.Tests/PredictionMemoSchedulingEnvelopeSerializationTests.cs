using System.Text.Json;
using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Identity;
using HorseRacingPrediction.Contracts.MachineLearning;
using HorseRacingPrediction.Contracts.Memos;
using HorseRacingPrediction.Contracts.PredictionScheduling;
using HorseRacingPrediction.Contracts.Predictions;
using HorseRacingPrediction.Contracts.Repairs;
using HorseRacingPrediction.Contracts.Subjects;

namespace HorseRacingPrediction.Contracts.Tests;

[TestClass]
public sealed class PredictionMemoSchedulingEnvelopeSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void BodyRequestsSerializeOnlyTheirFrozenOperationPayloadRoot()
    {
        var requests = new (object Request, string Root)[]
        {
            (new ResolveHorseIdentityRequest(new("Horse", "source", new DateOnly(2020, 1, 2))), "horse"),
            (new ResolveRaceIdentityRequest(new(new DateOnly(2026, 10, 1), "Tokyo", 1)), "race"),
            (new CreateMemoRequest(new("author", "Note", "content", DateTimeOffset.UnixEpoch, [])), "memo"),
            (new UpdateMemoRequest("route-memo", new(Content: "updated")), "memo"),
            (new ChangeMemoSubjectsRequest("route-memo", []), "subjects"),
            (new CreatePredictionTicketRequest(new("race", "Predictor", "model", .75m, null)), "ticket"),
            (new AddPredictionMarkRequest("route-ticket", new("entry", "◎", 1, 90m, null)), "mark"),
            (new AddBettingSuggestionRequest("route-ticket", new("WIN", "1", 10m, null)), "suggestion"),
            (new AddPredictionRationaleRequest("route-ticket", new("Horse", "horse", "Signal", null, null)), "rationale"),
            (new CorrectPredictionMetadataRequest("route-ticket", new(.5m, "summary", "reason")), "metadata"),
            (new EvaluatePredictionTicketRequest("route-ticket", new("race", DateTimeOffset.UnixEpoch, 1, [], null, null, null)), "evaluation"),
            (new RecalculatePredictionEvaluationRequest("route-ticket", new("race", DateTimeOffset.UnixEpoch, 1, [], null, null, null)), "evaluation"),
            (new WithdrawPredictionTicketRequest("route-ticket", new("reason")), "withdrawal"),
            (new AcquirePredictionCandidateLeasesRequest(new(DateTimeOffset.UnixEpoch, TimeSpan.Zero, 1, TimeSpan.FromMinutes(1))), "acquisition"),
            (new EnqueuePredictionCandidatesRequest(new(["race"], DateTimeOffset.UnixEpoch)), "candidates"),
            (new TransitionPredictionCandidateRequest("route-race", new("Complete", "lease")), "transition"),
            (new ApplyHorseIdentityRepairRequest(["candidate"]), "candidateIds"),
            (new ApplySubjectNameNormalizationRequest([]), "items"),
            (new DismissSubjectIdentificationFailuresRequest([Guid.Empty]), "notificationIds"),
            (new ExecuteSubjectIdentificationRepairRequest([]), "items"),
            (new PutSubjectProfileRequest("Horse", "route-subject", null), "profile"),
        };

        foreach (var (request, expectedRoot) in requests)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(request, JsonOptions));
            CollectionAssert.AreEqual(new[] { expectedRoot }, document.RootElement.EnumerateObject()
                .Select(property => property.Name).ToArray(), request.GetType().Name);
        }
    }

    [TestMethod]
    public void PathAndQueryRequestsDoNotLeakRouteMetadataIntoJsonBodies()
    {
        var requests = new object[]
        {
            new GetMlPredictionRequest("route-race"),
            new DeleteMemoRequest("route-memo"),
            new GetMemosBySubjectRequest("Race", "route-race"),
            new GetPredictionTicketRequest("route-ticket"),
            new FinalizePredictionTicketRequest("route-ticket"),
            new GetSubjectProfileRequest("Horse", "route-subject"),
        };

        foreach (var request in requests)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(request, JsonOptions));
            Assert.AreEqual(JsonValueKind.Object, document.RootElement.ValueKind, request.GetType().Name);
            Assert.AreEqual(0, document.RootElement.EnumerateObject().Count(), request.GetType().Name);
        }

        using var query = JsonDocument.Parse(JsonSerializer.Serialize(
            new GetSubjectNameNormalizationRequest(HorseRacingPrediction.Contracts.Collection.CollectionResourceType.Horse,
                "name", 2, 10), JsonOptions));
        CollectionAssert.AreEquivalent(new[] { "subjectType", "query", "page", "pageSize" },
            query.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
    }

    [TestMethod]
    public void DataResponsesSerializeOnlyTheirFrozenNamedRoot()
    {
        var responses = new (object Response, string Root)[]
        {
            (new ResolveHorseIdentityResponse(new("horse")), "identity"),
            (new ResolveRaceIdentityResponse(new("race")), "identity"),
            (new GetMlPredictionResponse(new("race", [])), "prediction"),
            (new TrainMlModelResponse(new(3, true)), "trainingResult"),
            (new CreateMemoResponse("memo"), "memoId"),
            (new GetMemosBySubjectResponse([]), "memos"),
            (new CreatePredictionTicketResponse("ticket"), "predictionTicketId"),
            (new GetPredictionTicketResponse(null!), "predictionTicket"),
            (new SearchPredictionTicketsResponse([], new(1, 20, 0, 0)), "predictionTickets"),
            (new AcquirePredictionCandidateLeasesResponse([]), "leases"),
            (new ApplyHorseIdentityRepairResponse(new("repair", 1, 0)), "application"),
            (new ApplySubjectNameNormalizationResponse(new(0, 0, 0, 0, [])), "normalization"),
            (new DismissSubjectIdentificationFailuresResponse(new(1, 1, 0)), "dismissal"),
            (new ExecuteSubjectIdentificationRepairResponse(new(1, 1, 0, [])), "execution"),
            (new GetHorseIdentityRepairResponse(null!), "preview"),
            (new GetSubjectIdentificationRepairResponse(null!), "preview"),
            (new GetSubjectNameNormalizationResponse(null!), "page"),
            (new GetSubjectProfileResponse(null!), "profile"),
        };

        foreach (var (response, expectedRoot) in responses)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonOptions));
            var expectedRoots = response is SearchPredictionTicketsResponse
                ? new[] { "predictionTickets", "pagination" }
                : new[] { expectedRoot };
            CollectionAssert.AreEqual(expectedRoots, document.RootElement.EnumerateObject()
                .Select(property => property.Name).ToArray(), response.GetType().Name);
        }
    }
}
