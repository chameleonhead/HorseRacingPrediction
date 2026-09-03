using HorseRacingPrediction.Scraping.Jra.Models;

namespace HorseRacingPrediction.Scraping.Tests.Models;

[TestClass]
public sealed class RaceCourseNamesTests
{
    [TestMethod]
    [DataRow("5 地方 中山 京成杯オータムH(GⅢ)", RaceCourse.Nakayama)]
    [DataRow("阪神競馬場 2026年9月6日", RaceCourse.Hanshin)]
    [DataRow("小倉開催", RaceCourse.Kokura)]
    [DataRow("地方競馬のみ", RaceCourse.Unknown)]
    public void Parse_ReturnsExpectedCourse(string text, RaceCourse expected)
    {
        Assert.AreEqual(expected, RaceCourseNames.Parse(text));
    }

    [TestMethod]
    public void ParseAll_ReturnsCoursesInAppearanceOrder()
    {
        var result =
            RaceCourseNames.ParseAll("5 地方 中山 京成杯オータムH(GⅢ) 阪神 札幌 札幌2歳S(GⅢ)");

        CollectionAssert.AreEqual(
            new[] { RaceCourse.Nakayama, RaceCourse.Hanshin, RaceCourse.Sapporo },
            result.ToArray());
    }

    [TestMethod]
    public void ParseAll_NoCourseFound_ReturnsEmpty()
    {
        var result =
            RaceCourseNames.ParseAll("5 地方競馬のみ");

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    [DataRow(RaceCourse.Sapporo, "札幌")]
    [DataRow(RaceCourse.Hakodate, "函館")]
    [DataRow(RaceCourse.Fukushima, "福島")]
    [DataRow(RaceCourse.Niigata, "新潟")]
    [DataRow(RaceCourse.Tokyo, "東京")]
    [DataRow(RaceCourse.Nakayama, "中山")]
    [DataRow(RaceCourse.Chukyo, "中京")]
    [DataRow(RaceCourse.Kyoto, "京都")]
    [DataRow(RaceCourse.Hanshin, "阪神")]
    [DataRow(RaceCourse.Kokura, "小倉")]
    public void GetJraName_ReturnsExpectedName(RaceCourse course, string expected)
    {
        Assert.AreEqual(expected, RaceCourseNames.GetJraName(course));
    }

    [TestMethod]
    public void GetJraName_Unknown_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => RaceCourseNames.GetJraName(RaceCourse.Unknown));
    }
}
