using HorseRacingPrediction.Agents.Plugins;

namespace HorseRacingPrediction.Agents.Tests;

/// <summary>
/// CalendarTools のユニットテスト。
/// TimeProvider のモックにより現在時刻を固定してテストする。
/// </summary>
[TestClass]
public class CalendarToolsTests
{
    // ------------------------------------------------------------------ //
    // GetCurrentDateTime
    // ------------------------------------------------------------------ //

    [TestMethod]
    public void GetCurrentDateTime_ReturnsJapaneseFormattedDateTime()
    {
        var fixedUtc = new DateTimeOffset(2024, 10, 24, 0, 0, 0, TimeSpan.Zero); // UTC 0:00 → JST 9:00
        var sut = new CalendarTools(new FakeTimeProvider(fixedUtc));

        var result = sut.GetCurrentDateTime();

        StringAssert.Contains(result, "2024年10月24日", "日付が含まれること");
        StringAssert.Contains(result, "木曜日", "曜日が含まれること");
        StringAssert.Contains(result, "9時0分", "JST 9:00 が含まれること");
    }

    [TestMethod]
    public void GetCurrentDateTime_ConvertsUtcToJst()
    {
        // UTC 2024-10-24 15:30 → JST 2024-10-25 00:30
        var fixedUtc = new DateTimeOffset(2024, 10, 24, 15, 30, 0, TimeSpan.Zero);
        var sut = new CalendarTools(new FakeTimeProvider(fixedUtc));

        var result = sut.GetCurrentDateTime();

        StringAssert.Contains(result, "2024年10月25日", "日付変更線を跨いだ場合に翌日になること");
        StringAssert.Contains(result, "0時30分", "JST 0:30 が含まれること");
    }

    // ------------------------------------------------------------------ //
    // GetWeekendDates
    // ------------------------------------------------------------------ //

    [TestMethod]
    public void GetWeekendDates_Thursday_ReturnsNextSaturday()
    {
        var sut = new CalendarTools();

        var result = sut.GetWeekendDates("2024-10-24"); // 木曜日

        StringAssert.Contains(result, "2024-10-26", "土曜日が返されること");
        StringAssert.Contains(result, "2024-10-27", "日曜日が返されること");
    }

    [TestMethod]
    public void GetWeekendDates_Saturday_ReturnsSameSaturday()
    {
        var sut = new CalendarTools();

        var result = sut.GetWeekendDates("2024-10-26"); // 土曜日

        StringAssert.Contains(result, "2024-10-26", "土曜日自体が返されること");
        StringAssert.Contains(result, "2024-10-27", "日曜日が返されること");
    }

    [TestMethod]
    public void GetWeekendDates_Sunday_ReturnsSameWeekend()
    {
        var sut = new CalendarTools();

        var result = sut.GetWeekendDates("2024-10-27"); // 日曜日

        StringAssert.Contains(result, "2024-10-26", "前日の土曜日が返されること");
        StringAssert.Contains(result, "2024-10-27", "日曜日が返されること");
    }

    [TestMethod]
    public void GetWeekendDates_Monday_ReturnsNextSaturday()
    {
        var sut = new CalendarTools();

        var result = sut.GetWeekendDates("2024-10-21"); // 月曜日

        StringAssert.Contains(result, "2024-10-26", "次の土曜日が返されること");
        StringAssert.Contains(result, "2024-10-27", "次の日曜日が返されること");
    }

    [TestMethod]
    public void GetWeekendDates_NoArgument_UsesCurrentDate()
    {
        var fixedUtc = new DateTimeOffset(2024, 10, 24, 0, 0, 0, TimeSpan.Zero);
        var sut = new CalendarTools(new FakeTimeProvider(fixedUtc));

        var result = sut.GetWeekendDates();

        StringAssert.Contains(result, "2024-10-26", "今週の土曜日が返されること");
    }

    // ------------------------------------------------------------------ //
    // GetJraRacecourseCode
    // ------------------------------------------------------------------ //

    [TestMethod]
    public void GetJraRacecourseCode_Tokyo_Returns05()
    {
        var sut = new CalendarTools();

        var result = sut.GetJraRacecourseCode("東京");

        StringAssert.Contains(result, "05", "東京の競馬場コード 05 が返されること");
    }

    [TestMethod]
    public void GetJraRacecourseCode_Nakayama_Returns06()
    {
        var sut = new CalendarTools();

        var result = sut.GetJraRacecourseCode("中山");

        StringAssert.Contains(result, "06", "中山の競馬場コード 06 が返されること");
    }

    [TestMethod]
    public void GetJraRacecourseCode_AllCourses_ReturnValidCodes()
    {
        var sut = new CalendarTools();
        var expected = new Dictionary<string, string>
        {
            ["札幌"] = "01", ["函館"] = "02", ["福島"] = "03", ["新潟"] = "04",
            ["東京"] = "05", ["中山"] = "06", ["中京"] = "07", ["京都"] = "08",
            ["阪神"] = "09", ["小倉"] = "10"
        };

        foreach (var (name, code) in expected)
        {
            var result = sut.GetJraRacecourseCode(name);
            StringAssert.Contains(result, code, $"{name} のコード {code} が含まれること");
        }
    }

    [TestMethod]
    public void GetJraRacecourseCode_Unknown_ReturnsNotFoundWithAvailableList()
    {
        var sut = new CalendarTools();

        var result = sut.GetJraRacecourseCode("大井");

        StringAssert.Contains(result, "見つかりませんでした", "見つからないメッセージが含まれること");
        StringAssert.Contains(result, "東京", "利用可能な競馬場名が含まれること");
    }

    [TestMethod]
    public void GetJraRacecourseCode_TrimmedInput_Works()
    {
        var sut = new CalendarTools();

        var result = sut.GetJraRacecourseCode(" 阪神 ");

        StringAssert.Contains(result, "09", "前後の空白を除去して処理されること");
    }

    // ------------------------------------------------------------------ //
    // GetAITools
    // ------------------------------------------------------------------ //

    [TestMethod]
    public void GetAITools_Returns3Tools()
    {
        var sut = new CalendarTools();

        var tools = sut.GetAITools();

        Assert.AreEqual(3, tools.Count, "3 つのツールが返されること");
    }

    // ------------------------------------------------------------------ //
    // Fake TimeProvider
    // ------------------------------------------------------------------ //

    private sealed class FakeTimeProvider(DateTimeOffset fixedUtcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => fixedUtcNow;
    }
}
