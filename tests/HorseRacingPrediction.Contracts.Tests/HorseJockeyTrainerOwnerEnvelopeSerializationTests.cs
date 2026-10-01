using System.Text.Json;

using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Horses;
using HorseRacingPrediction.Contracts.Jockeys;
using HorseRacingPrediction.Contracts.Owners;
using HorseRacingPrediction.Contracts.Trainers;

namespace HorseRacingPrediction.Contracts.Tests;

[TestClass]
public sealed class HorseJockeyTrainerOwnerEnvelopeSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void AllFifteenBodyOperationsSerializeTheirFrozenPayloadPropertyAndOmitPathMetadata()
    {
        var requests = new (object Value, string Property, string? IgnoredPathProperty)[]
        {
            (new RegisterHorseRequest(new RegisterHorseInputDto("Horse", "horse", null, null)), "horse", null),
            (new CorrectHorseDataRequest { HorseId = "h-1", Horse = new(null, null, null, null, "reason") }, "horse", "horseId"),
            (new UpdateHorseProfileRequest { HorseId = "h-1", Horse = new(null, null, null, null) }, "horse", "horseId"),
            (new MergeHorseAliasRequest { HorseId = "h-1", Alias = new("official", "H-1", "JRA", false) }, "alias", "horseId"),
            (new RegisterJockeyRequest(new RegisterJockeyInputDto("Jockey", "jockey", null)), "jockey", null),
            (new CorrectJockeyDataRequest { JockeyId = "j-1", Jockey = new(null, null, null, "reason") }, "jockey", "jockeyId"),
            (new UpdateJockeyProfileRequest { JockeyId = "j-1", Jockey = new(null, null, null) }, "jockey", "jockeyId"),
            (new MergeJockeyAliasRequest { JockeyId = "j-1", Alias = new("official", "J-1", "JRA", false) }, "alias", "jockeyId"),
            (new RegisterTrainerRequest(new RegisterTrainerInputDto("Trainer", "trainer", null)), "trainer", null),
            (new CorrectTrainerDataRequest { TrainerId = "t-1", Trainer = new(null, null, null, "reason") }, "trainer", "trainerId"),
            (new UpdateTrainerProfileRequest { TrainerId = "t-1", Trainer = new(null, null, null) }, "trainer", "trainerId"),
            (new MergeTrainerAliasRequest { TrainerId = "t-1", Alias = new("official", "T-1", "JRA", false) }, "alias", "trainerId"),
            (new UpdateOwnerRequest { OwnerId = "o-1", Owner = new("Owner", "reason") }, "owner", "ownerId"),
            (new MergeOwnerRequest { OwnerId = "o-1", Merge = new("source", "reason") }, "merge", "ownerId"),
            (new ExecuteOwnerIdentityRecoveryRequest([new(Guid.NewGuid(), "fingerprint")]), "items", null)
        };

        foreach (var (value, property, ignoredPathProperty) in requests)
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(value, JsonOptions));
            Assert.IsTrue(json.RootElement.TryGetProperty(property, out _), value.GetType().Name);
            Assert.AreEqual(1, json.RootElement.EnumerateObject().Count(), value.GetType().Name);
            if (ignoredPathProperty is not null)
                Assert.IsFalse(json.RootElement.TryGetProperty(ignoredPathProperty, out _), value.GetType().Name);
        }
    }

    [TestMethod]
    public void AllNineteenDataResponsesSerializeTheirFrozenTopLevelPropertyNames()
    {
        var responses = new (object Value, string[] Properties)[]
        {
            (new SearchHorsesResponse([], new(1, 20, 0, 0)), ["horses", "pagination"]),
            (new RegisterHorseResponse("h-1"), ["horseId"]),
            (new GetHorseProfileResponse(null!), ["horse"]),
            (new GetHorseParticipationsResponse(null!), ["history"]),
            (new GetHorseRaceHistoryResponse(null!), ["history"]),
            (new GetHorseWeightHistoryResponse(null!), ["weightHistory"]),
            (new SearchJockeysResponse([], new(1, 20, 0, 0)), ["jockeys", "pagination"]),
            (new RegisterJockeyResponse("j-1"), ["jockeyId"]),
            (new GetJockeyProfileResponse(null!), ["jockey"]),
            (new GetJockeyParticipationsResponse(null!), ["history"]),
            (new GetJockeyRaceHistoryResponse(null!), ["history"]),
            (new SearchTrainersResponse([], new(1, 20, 0, 0)), ["trainers", "pagination"]),
            (new RegisterTrainerResponse("t-1"), ["trainerId"]),
            (new GetTrainerProfileResponse(null!), ["trainer"]),
            (new GetTrainerParticipationsResponse(null!), ["history"]),
            (new PreviewOwnerIdentityRecoveryResponse([]), ["candidates"]),
            (new ExecuteOwnerIdentityRecoveryResponse([new CollectionRequestReceiptDto(Guid.NewGuid(), null, false)]), ["receipts"]),
            (new SearchOwnersResponse([]), ["owners"]),
            (new GetOwnerResponse(null!), ["owner"])
        };

        foreach (var (value, properties) in responses)
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(value, JsonOptions));
            CollectionAssert.AreEquivalent(properties, json.RootElement.EnumerateObject().Select(x => x.Name).ToArray(), value.GetType().Name);
        }
    }
}
