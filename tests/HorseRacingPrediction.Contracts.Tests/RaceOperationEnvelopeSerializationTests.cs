using System.Text.Json;

using HorseRacingPrediction.Contracts.Races;

namespace HorseRacingPrediction.Contracts.Tests;

[TestClass]
public sealed class RaceOperationEnvelopeSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void RequestEnvelopesSerializeOnlyTheirNestedOperationPayload()
    {
        var createRace = new CreateRaceRequest(new CreateRaceInputDto(
            new DateOnly(2026, 9, 30), "TOKYO", 4, "秋のテスト", "body-race-id"));
        using var createJson = JsonDocument.Parse(JsonSerializer.Serialize(createRace, JsonOptions));
        Assert.HasCount(1, createJson.RootElement.EnumerateObject());
        Assert.AreEqual("body-race-id", createJson.RootElement.GetProperty("race").GetProperty("raceId").GetString());

        var registerEntry = new RegisterEntryRequest(new RegisterEntryInputDto(
            "horse-1", 3, null, null, 2, 55m, "M", 3, 470m, 0m))
        { RaceId = "route-race-id" };
        using var entryJson = JsonDocument.Parse(JsonSerializer.Serialize(registerEntry, JsonOptions));
        Assert.HasCount(1, entryJson.RootElement.EnumerateObject());
        Assert.IsFalse(entryJson.RootElement.TryGetProperty("raceId", out _));
        Assert.AreEqual("horse-1", entryJson.RootElement.GetProperty("entry").GetProperty("horseId").GetString());

        var bulk = new DeclareRaceResultBulkRequest(new DeclareRaceResultBulkInputDto(
            new DateOnly(2026, 9, 30), "TOKYO", 4, "秋のテスト", EntryCount: 1, IsRaceCard: true));
        using var bulkJson = JsonDocument.Parse(JsonSerializer.Serialize(bulk, JsonOptions));
        Assert.HasCount(1, bulkJson.RootElement.EnumerateObject());
        Assert.AreEqual(1, bulkJson.RootElement.GetProperty("result").GetProperty("entryCount").GetInt32());
    }

    [TestMethod]
    public void DataResponsesSerializeUnderNamedResponseProperty()
    {
        var bulkResponse = new DeclareRaceResultBulkResponse(new DeclareRaceResultBulkResultDto(
            "race-1", ["accepted"], CorePersisted: true));
        using var bulkJson = JsonDocument.Parse(JsonSerializer.Serialize(bulkResponse, JsonOptions));
        Assert.HasCount(1, bulkJson.RootElement.EnumerateObject());
        var result = bulkJson.RootElement.GetProperty("result");
        Assert.AreEqual("race-1", result.GetProperty("raceId").GetString());
        Assert.IsTrue(result.GetProperty("corePersisted").GetBoolean());
        Assert.AreEqual("accepted", result.GetProperty("errors")[0].GetString());

        var response = new GetRaceResponse(new RaceDto(
            "race-1", null, null, null, null, RaceStatus.Draft, null, null, null, null,
            null, null, null, [], [], [], null, null, null, null, null, [], null));
        using var raceJson = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonOptions));
        Assert.HasCount(1, raceJson.RootElement.EnumerateObject());
        Assert.AreEqual("race-1", raceJson.RootElement.GetProperty("race").GetProperty("raceId").GetString());
    }
}
