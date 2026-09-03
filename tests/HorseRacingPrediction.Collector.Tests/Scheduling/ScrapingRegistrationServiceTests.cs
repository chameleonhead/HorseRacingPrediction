// JRAサイト再設計（docs/jra-scraping.md）により、対象の ScrapingRegistrationService は一時的に無効化されている。
#if false
using HorseRacingPrediction.Collector.Scheduling;

namespace HorseRacingPrediction.Collector.Tests.Scheduling;

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

        Assert.IsEmpty(result);
    }
}
#endif