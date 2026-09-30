using System.Text.Json;

using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Horses;
using HorseRacingPrediction.Contracts.Jockeys;
using HorseRacingPrediction.Contracts.Trainers;

namespace HorseRacingPrediction.Contracts.Tests;

[TestClass]
public sealed class MergedProfileDtoSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void HorseDto_RoundTripsExistingProfilePayloadAndNullableAliasSource()
    {
        const string profileJson = """
            {"horseId":"h-1","registeredName":"Horse One","normalizedName":"horse one","sexCode":null,"birthDate":"2020-01-02","aliases":[{"aliasType":"registration","aliasValue":"R-1","sourceName":null,"isPrimary":true}],"ownerName":null,"breederName":"Farm","sireName":null,"damName":"Dam","damsireName":null,"coatColor":"bay"}
            """;

        var profile = JsonSerializer.Deserialize<HorseDto>(profileJson, JsonOptions)!;
        var roundTrip = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(profile, JsonOptions));

        Assert.AreEqual("Horse One", profile.RegisteredName);
        Assert.AreEqual(new DateOnly(2020, 1, 2), profile.BirthDate);
        Assert.IsNull(profile.SexCode);
        Assert.IsNull(profile.OwnerName);
        Assert.AreEqual("Farm", profile.BreederName);
        Assert.AreEqual("Dam", profile.DamName);
        Assert.AreEqual("bay", profile.CoatColor);
        Assert.AreEqual(1, profile.Aliases.Count);
        Assert.IsNull(profile.Aliases[0].SourceName);
        Assert.IsTrue(roundTrip.GetProperty("sexCode").ValueKind == JsonValueKind.Null);
        Assert.IsTrue(roundTrip.GetProperty("ownerName").ValueKind == JsonValueKind.Null);
        Assert.IsTrue(roundTrip.GetProperty("aliases")[0].GetProperty("sourceName").ValueKind == JsonValueKind.Null);
    }

    [TestMethod]
    public void JockeyDto_RoundTripsExistingProfilePayloadAndNullableAliasSource()
    {
        const string profileJson = """
            {"jockeyId":"j-1","displayName":"Jockey One","normalizedName":"jockey one","affiliationCode":null,"aliases":[{"aliasType":"official","aliasValue":"J-1","sourceName":null,"isPrimary":true}]}
            """;

        var profile = JsonSerializer.Deserialize<JockeyDto>(profileJson, JsonOptions)!;
        var roundTrip = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(profile, JsonOptions));

        Assert.AreEqual("Jockey One", profile.DisplayName);
        Assert.IsNull(profile.AffiliationCode);
        Assert.AreEqual(1, profile.Aliases.Count);
        Assert.IsNull(profile.Aliases[0].SourceName);
        Assert.IsTrue(roundTrip.GetProperty("affiliationCode").ValueKind == JsonValueKind.Null);
        Assert.IsTrue(roundTrip.GetProperty("aliases")[0].GetProperty("sourceName").ValueKind == JsonValueKind.Null);
    }

    [TestMethod]
    public void TrainerDto_RoundTripsExistingProfilePayloadAndNullableAliasSource()
    {
        const string profileJson = """
            {"trainerId":"t-1","displayName":"Trainer One","normalizedName":"trainer one","affiliationCode":"Miho","aliases":[{"aliasType":"official","aliasValue":"T-1","sourceName":null,"isPrimary":true}]}
            """;

        var profile = JsonSerializer.Deserialize<TrainerDto>(profileJson, JsonOptions)!;
        var roundTrip = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(profile, JsonOptions));

        Assert.AreEqual("Trainer One", profile.DisplayName);
        Assert.AreEqual("Miho", profile.AffiliationCode);
        Assert.AreEqual(1, profile.Aliases.Count);
        Assert.IsNull(profile.Aliases[0].SourceName);
        Assert.AreEqual("Miho", roundTrip.GetProperty("affiliationCode").GetString());
        Assert.IsTrue(roundTrip.GetProperty("aliases")[0].GetProperty("sourceName").ValueKind == JsonValueKind.Null);
    }
}
