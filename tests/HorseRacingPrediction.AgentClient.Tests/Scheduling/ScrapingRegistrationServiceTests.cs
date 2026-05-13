using HorseRacingPrediction.AgentClient.Scheduling;

namespace HorseRacingPrediction.AgentClient.Tests.Scheduling;

[TestClass]
public sealed class ScrapingRegistrationServiceTests
{
    [TestMethod]
    public void BuildPredictionCandidateRaceIds_RemovesDuplicatesAndBlanks()
    {
        var result = ScrapingRegistrationService.BuildPredictionCandidateRaceIds(
            ["race-1", "", "race-2", "race-1", " ", "race-3"]);

        CollectionAssert.AreEqual(new[] { "race-1", "race-2", "race-3" }, result.ToArray());
    }

    [TestMethod]
    public void BuildPredictionCandidateRaceIds_EmptyInput_ReturnsEmptyList()
    {
        var result = ScrapingRegistrationService.BuildPredictionCandidateRaceIds([]);

        Assert.AreEqual(0, result.Count);
    }
}