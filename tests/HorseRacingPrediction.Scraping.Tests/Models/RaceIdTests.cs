using HorseRacingPrediction.Scraping.Jra.Models;

namespace HorseRacingPrediction.Scraping.Tests.Models;

[TestClass]
public sealed class RaceIdTests
{
    [TestMethod]
    public void Constructor_ValidNumber_SetsProperties()
    {
        var raceId = new RaceId(
            new DateOnly(2026, 9, 6),
            RaceCourse.Nakayama,
            11);

        Assert.AreEqual(new DateOnly(2026, 9, 6), raceId.Date);
        Assert.AreEqual(RaceCourse.Nakayama, raceId.Course);
        Assert.AreEqual(11, raceId.Number);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(13)]
    public void Constructor_NumberOutOfRange_Throws(int number)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new RaceId(
                new DateOnly(2026, 9, 6),
                RaceCourse.Nakayama,
                number));
    }
}
