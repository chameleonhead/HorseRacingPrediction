using HorseRacingPrediction.Scraping.Jra.Models;

namespace HorseRacingPrediction.Scraping.Tests.Models;

[TestClass]
public sealed class YearMonthTests
{
    [TestMethod]
    public void Constructor_ValidYearMonth_SetsProperties()
    {
        var month = new YearMonth(2026, 9);

        Assert.AreEqual(2026, month.Year);
        Assert.AreEqual(9, month.Month);
    }

    [TestMethod]
    [DataRow(1899)]
    [DataRow(2201)]
    public void Constructor_YearOutOfRange_Throws(int year)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new YearMonth(year, 1));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(13)]
    public void Constructor_MonthOutOfRange_Throws(int month)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new YearMonth(2026, month));
    }

    [TestMethod]
    public void FirstDay_ReturnsFirstDayOfMonth()
    {
        var month = new YearMonth(2026, 9);

        Assert.AreEqual(new DateOnly(2026, 9, 1), month.FirstDay);
    }

    [TestMethod]
    public void ToString_ReturnsYyyyMmFormat()
    {
        var month = new YearMonth(2026, 9);

        Assert.AreEqual("2026-09", month.ToString());
    }
}
