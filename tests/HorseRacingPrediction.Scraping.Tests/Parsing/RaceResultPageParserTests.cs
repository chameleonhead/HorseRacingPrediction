using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;

namespace HorseRacingPrediction.Scraping.Tests.Parsing;

[TestClass]
public sealed class RaceResultPageParserTests
{
    private const string Url = "https://www.jra.go.jp/keiba/sample/raceresult/";

    private static PageSnapshot BuildSnapshot()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows:
            [
                ["1", "3", "テストホースC", "騎手C", "1:33.4"],
                ["2", "1", "テストホースA", "騎手A", "1:33.6"],
                ["3", "2", "テストホースB", "騎手B", "1:33.9"],
            ]);

        var payoutTable = new PageTableSnapshot(
            Headers: ["式別", "組合せ", "払戻金"],
            Rows:
            [
                ["単勝", "3", "250円"],
                ["複勝", "3\n1\n2", "120円\n110円\n130円"],
                ["馬連", "1-3", "450円"],
                ["馬単", "3-1", "780円"],
                ["三連単", "3-1-2", "3,120円"],
            ]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            // 実サイト確認（2026-09-07）で判明した実際の表記に合わせる:
            // 「馬場」「馬場状態」という語を伴わず、「芝」「ダート」の直後に状態値が続く。
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table, payoutTable],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス(GⅢ)"]);

        return new PageSnapshot(Url, "2026年9月5日 中山 11R テストステークス 結果", [section]);
    }

    [TestMethod]
    public void Parse_ResultTimesAndEstimatedFurlongColumnAreNotRaceStartOrPace()
    {
        var original = BuildSnapshot();
        var snapshot = new PageSnapshot(Url, original.Title, [new PageSectionSnapshot("結果",
            "推定上り 馬体重 調教師名 1 3 テストホース 1:33.4", [], [], original.Tables, original.Headings)]);
        var page = (JraRaceResultPage)new RaceResultPageParser().Parse(snapshot);
        Assert.IsNull(page.StartTime);
        Assert.IsNull(page.OverallPaceText);
    }

    [TestMethod]
    public void Parse_RowHeadersAndEstimatedLastFurlongHeadingDoNotCorruptRaceMetadata()
    {
        var original = BuildSnapshot();
        var time = new PageTableSnapshot(["ハロンタイム", ""],
            [["ハロンタイム", "12.7 - 11.8 - 13.3"], ["上り", "4F 50.2 - 3F 37.9"]]);
        var corners = new PageTableSnapshot(["1コーナー", "7(8,12)"],
            [["1コーナー", "7(8,12)"], ["2コーナー", "7-8(10,9,12)"], ["3コーナー", "7,8,12"], ["4コーナー", "7,8=12"]]);
        var snapshot = new PageSnapshot(Url, original.Title, [new PageSectionSnapshot("結果",
            "発走時刻：13時00分 推定上り 馬体重 （増減） 調教師名 単勝 人気 1 8 テストホース",
            [], [], [..original.Tables, time, corners], original.Headings)]);
        var page = (JraRaceResultPage)new RaceResultPageParser().Parse(snapshot);
        Assert.AreEqual(new TimeOnly(13, 0), page.StartTime);
        Assert.AreEqual("ハロンタイム: 12.7 - 11.8 - 13.3 / 上り: 4F 50.2 - 3F 37.9", page.OverallPaceText);
        Assert.AreEqual(4, page.CornerPassages!.Count);
        Assert.AreEqual("7(8,12)", page.CornerPassages[0].OrderRaw);
        Assert.AreEqual(4, page.CornerPassages[3].CornerNumber);
    }

    [TestMethod]
    [DataRow("メイクデビュー中山")]
    [DataRow("メイクデビュー阪神")]
    [DataRow("東京優駿(GⅠ)")]
    public void Parse_CourseNameWithinRaceName_PreservesFullName(string name)
    {
        var snapshot = BuildSnapshot();
        snapshot.Headings.Clear();
        snapshot.Headings.AddRange(["JRA 日本中央競馬会", "レース結果2026年9月6日（日曜）4回中山2日 6レース", "中山", "4回中山2日", name, "払戻金", "勝馬の紹介", "エンジャムメント 2024年4月11日生牝2"]);
        var page = (JraRaceResultPage)new RaceResultPageParser().Parse(snapshot);
        Assert.AreEqual(name, page.RaceName);
    }

    [TestMethod]
    public void Parse_MissingRaceName_DoesNotUseWinningHorseHeading()
    {
        var snapshot = BuildSnapshot();
        snapshot.Headings.Clear();
        snapshot.Headings.AddRange(["2026年9月6日 中山 6R", "払戻金", "勝馬の紹介", "エンジャムメント 2024年4月11日生牝2"]);
        Assert.ThrowsExactly<JraPageParseException>(() => new RaceResultPageParser().Parse(snapshot));
    }

    [TestMethod]
    public void CanParse_TableWithResultColumns_ReturnsTrue()
    {
        var parser = new RaceResultPageParser();

        Assert.IsTrue(parser.CanParse(BuildSnapshot()));
    }

    [TestMethod]
    public void CanParse_NoResultTable_ReturnsFalse()
    {
        var section = new PageSectionSnapshot(
            title: "無関係ページ",
            mainText: string.Empty,
            links: [],
            actions: [],
            tables: [],
            headings: []);

        var snapshot = new PageSnapshot(Url, "無関係ページ", [section]);

        var parser = new RaceResultPageParser();

        Assert.IsFalse(parser.CanParse(snapshot));
    }

    [TestMethod]
    public void Parse_ReturnsExpectedRaceResult()
    {
        var parser = new RaceResultPageParser();

        var page = (JraRaceResultPage)parser.Parse(BuildSnapshot());

        Assert.AreEqual(new DateOnly(2026, 9, 5), page.RaceId.Date);
        Assert.AreEqual(RaceCourse.Nakayama, page.RaceId.Course);
        Assert.AreEqual(11, page.RaceId.Number);

        Assert.AreEqual(3, page.Results.Count);

        Assert.AreEqual(1, page.Results[0].FinishPosition);
        Assert.AreEqual(3, page.Results[0].HorseNumber);
        Assert.AreEqual("テストホースC", page.Results[0].HorseName);
        Assert.AreEqual("騎手C", page.Results[0].JockeyName);
        Assert.AreEqual(new TimeSpan(0, 0, 1, 33, 400), page.Results[0].Time);

        Assert.AreEqual(2, page.Results[1].FinishPosition);
        Assert.AreEqual(1, page.Results[1].HorseNumber);
        Assert.AreEqual("テストホースA", page.Results[1].HorseName);

        Assert.AreEqual(3, page.Results[2].FinishPosition);
        Assert.AreEqual(2, page.Results[2].HorseNumber);
        Assert.AreEqual("テストホースB", page.Results[2].HorseName);

        Assert.AreEqual("テストステークス(GⅢ)", page.RaceName);
        Assert.AreEqual("晴", page.WeatherText);
        Assert.AreEqual("芝:良", page.TrackConditionText);

        Assert.IsNotNull(page.Payouts);
        Assert.AreEqual(1, page.Payouts!.WinPayouts.Count);
        Assert.AreEqual("3", page.Payouts.WinPayouts[0].Combination);
        Assert.AreEqual(250m, page.Payouts.WinPayouts[0].Amount);

        Assert.AreEqual(3, page.Payouts.PlacePayouts.Count);
        Assert.AreEqual("3", page.Payouts.PlacePayouts[0].Combination);
        Assert.AreEqual(120m, page.Payouts.PlacePayouts[0].Amount);
        Assert.AreEqual("1", page.Payouts.PlacePayouts[1].Combination);
        Assert.AreEqual(110m, page.Payouts.PlacePayouts[1].Amount);

        Assert.AreEqual(1, page.Payouts.QuinellaPayouts.Count);
        Assert.AreEqual("1-3", page.Payouts.QuinellaPayouts[0].Combination);
        Assert.AreEqual(450m, page.Payouts.QuinellaPayouts[0].Amount);

        Assert.AreEqual(1, page.Payouts.ExactaPayouts.Count);
        Assert.AreEqual(780m, page.Payouts.ExactaPayouts[0].Amount);

        Assert.AreEqual(1, page.Payouts.TrifectaPayouts.Count);
        Assert.AreEqual("3-1-2", page.Payouts.TrifectaPayouts[0].Combination);
        Assert.AreEqual(3120m, page.Payouts.TrifectaPayouts[0].Amount);
    }

    [TestMethod]
    public void Parse_障害レースで芝とダートの両方の馬場状態を取得できる()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows: [["1", "1", "テストホースA", "騎手A", "3:19.8"]]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 雨 芝 稍重 ダート 重",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月6日 中山 1R", "障害3歳以上未勝利"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var parser = new RaceResultPageParser();
        var page = (JraRaceResultPage)parser.Parse(snapshot);

        Assert.AreEqual("障害3歳以上未勝利", page.RaceName);
        Assert.AreEqual("芝:稍重 ダート:重", page.TrackConditionText);
    }

    [TestMethod]
    public void Parse_マストヘッド見出しのみでレース名を特定できない場合は例外を投げる()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows: [["1", "1", "テストホースA", "騎手A", "1:33.4"]]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table],
            // 日付・競馬場・レース番号は取得できるが、その直後にレース名として扱える
            // 見出しが続かないケース（想定外のページ構造）。
            headings: ["JRA 日本中央競馬会", "2026年9月6日 中山 1R"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var parser = new RaceResultPageParser();

        var ex = Assert.ThrowsExactly<JraPageParseException>(() => parser.Parse(snapshot));
        StringAssert.Contains(ex.Message, "レース名");
    }

    [TestMethod]
    public void Parse_取消除外中止失格を正常に解析できる()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows:
            [
                ["1", "1", "テストホースA", "騎手A", "1:33.4"],
                ["取消", "2", "テストホースB", "騎手B", string.Empty],
                ["除外", "3", "テストホースC", "騎手C", string.Empty],
                ["中止", "4", "テストホースD", "騎手D", string.Empty],
                ["失格", "5", "テストホースE", "騎手E", "1:35.0"],
            ]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var page = (JraRaceResultPage)new RaceResultPageParser().Parse(snapshot);

        Assert.AreEqual(5, page.Results.Count);

        Assert.AreEqual(ResultStatus.Finished, page.Results[0].ResultStatus);
        Assert.AreEqual(1, page.Results[0].FinishPosition);

        Assert.AreEqual(ResultStatus.Cancelled, page.Results[1].ResultStatus);
        Assert.IsNull(page.Results[1].FinishPosition);
        Assert.IsNull(page.Results[1].Time);

        Assert.AreEqual(ResultStatus.Excluded, page.Results[2].ResultStatus);
        Assert.IsNull(page.Results[2].FinishPosition);

        Assert.AreEqual(ResultStatus.DidNotFinish, page.Results[3].ResultStatus);
        Assert.IsNull(page.Results[3].FinishPosition);

        // 失格馬にタイムが存在しても正常（依頼書19節）。
        Assert.AreEqual(ResultStatus.Disqualified, page.Results[4].ResultStatus);
        Assert.IsNull(page.Results[4].FinishPosition);
        Assert.IsNotNull(page.Results[4].Time);
    }

    [TestMethod]
    public void Parse_着順欄が未知の値の場合はJraUnexpectedValueExceptionを投げる()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows: [["再検討", "1", "テストホースA", "騎手A", "1:33.4"]]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var ex = Assert.ThrowsExactly<JraUnexpectedValueException>(
            () => new RaceResultPageParser().Parse(snapshot));

        Assert.AreEqual("ResultStatus", ex.FieldName);
        Assert.AreEqual("再検討", ex.RawValue);
    }

    [TestMethod]
    public void Parse_天候が未知の値の場合はJraUnexpectedValueExceptionを投げる()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows: [["1", "1", "テストホースA", "騎手A", "1:33.4"]]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 不明 芝 良",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var ex = Assert.ThrowsExactly<JraUnexpectedValueException>(
            () => new RaceResultPageParser().Parse(snapshot));

        Assert.AreEqual("Weather", ex.FieldName);
        Assert.AreEqual("不明", ex.RawValue);
    }

    [TestMethod]
    public void Parse_馬場状態が未知の値の場合はJraUnexpectedValueExceptionを投げる()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows: [["1", "1", "テストホースA", "騎手A", "1:33.4"]]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 極重",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var ex = Assert.ThrowsExactly<JraUnexpectedValueException>(
            () => new RaceResultPageParser().Parse(snapshot));

        Assert.AreEqual("TrackCondition(Turf)", ex.FieldName);
        Assert.AreEqual("極重", ex.RawValue);
    }

    [TestMethod]
    public void Parse_性齢列がある場合はSexとAgeへ分解できる()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "性齢", "騎手", "タイム"],
            Rows:
            [
                ["1", "1", "テストホースA", "牡6", "騎手A", "1:33.4"],
                ["2", "2", "テストホースB", "牝5", "騎手B", "1:33.6"],
                ["3", "3", "テストホースC", "せん5", "騎手C", "1:33.9"],
            ]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var page = (JraRaceResultPage)new RaceResultPageParser().Parse(snapshot);

        Assert.AreEqual(HorseSex.Colt, page.Results[0].Sex);
        Assert.AreEqual(6, page.Results[0].Age);

        Assert.AreEqual(HorseSex.Filly, page.Results[1].Sex);
        Assert.AreEqual(5, page.Results[1].Age);

        Assert.AreEqual(HorseSex.Gelding, page.Results[2].Sex);
        Assert.AreEqual(5, page.Results[2].Age);
    }

    [TestMethod]
    public void Parse_性齢列の値が未知の場合はJraUnexpectedValueExceptionを投げる()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "性齢", "騎手", "タイム"],
            Rows: [["1", "1", "テストホースA", "騙5", "騎手A", "1:33.4"]]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var ex = Assert.ThrowsExactly<JraUnexpectedValueException>(
            () => new RaceResultPageParser().Parse(snapshot));

        Assert.AreEqual("Sex", ex.FieldName);
    }

    [TestMethod]
    public void Parse_天皇賞春の芝右外を方向とLayoutへ分解する()
    {
        var page = ParseWithMainText("天候 晴 芝 良 3,200メートル（芝・右 外）");

        Assert.IsNotNull(page.CourseSpec);
        Assert.AreEqual(3200, page.CourseSpec.DistanceMeters);
        CollectionAssert.AreEqual(new[] { CourseSurface.Turf }, page.CourseSpec.Surfaces.ToArray());
        Assert.AreEqual(CourseDirection.Right, page.CourseSpec.Direction);
        Assert.AreEqual("外", page.CourseSpec.Layout);
        Assert.AreEqual("芝・右 外", page.CourseSpec.RawLayout);
    }

    [TestMethod]
    public void Parse_未知の方向は外回り表記を伴っても拒否する()
    {
        var ex = Assert.ThrowsExactly<JraUnexpectedValueException>(
            () => ParseWithMainText("天候 晴 芝 良 3,200メートル（芝・北 外）"));
        Assert.AreEqual("Course.Direction", ex.FieldName);
    }

    [TestMethod]
    public void Parse_コース表記_芝左を分解できる()
    {
        var page = ParseWithMainText("天候 晴 芝 良 1,600メートル（芝・左）");

        Assert.IsNotNull(page.CourseSpec);
        Assert.AreEqual(1600, page.CourseSpec!.DistanceMeters);
        Assert.AreEqual(RaceType.Flat, page.CourseSpec.RaceType);
        CollectionAssert.AreEqual(new[] { CourseSurface.Turf }, page.CourseSpec.Surfaces.ToArray());
        Assert.AreEqual(CourseDirection.Left, page.CourseSpec.Direction);
        Assert.IsNull(page.CourseSpec.Layout);
        Assert.AreEqual("芝・左", page.CourseSpec.RawLayout);
    }

    [TestMethod]
    public void Parse_コース表記_ダート左を分解できる()
    {
        var page = ParseWithMainText("天候 晴 ダート 良 1,400メートル（ダート・左）");

        Assert.IsNotNull(page.CourseSpec);
        Assert.AreEqual(1400, page.CourseSpec!.DistanceMeters);
        CollectionAssert.AreEqual(new[] { CourseSurface.Dirt }, page.CourseSpec.Surfaces.ToArray());
        Assert.AreEqual(CourseDirection.Left, page.CourseSpec.Direction);
    }

    [TestMethod]
    public void Parse_コース表記_芝右外は方向とレイアウトの両方を分解できる()
    {
        // 実サイトE2E（過去レース結果、中山）で判明した表記。「芝・右」（方向）の後ろへ
        // 半角スペース区切りで「外」（外回り）が続く。従来は「・」区切りの後続部分を
        // 「左」「右」との完全一致でしか判定していなかったため、この表記で
        // Course.Directionが未知値としてエラーになっていた。
        var page = ParseWithMainText("天候 晴 芝 良 1,600メートル（芝・右 外）");

        Assert.IsNotNull(page.CourseSpec);
        Assert.AreEqual(1600, page.CourseSpec!.DistanceMeters);
        CollectionAssert.AreEqual(new[] { CourseSurface.Turf }, page.CourseSpec.Surfaces.ToArray());
        Assert.AreEqual(CourseDirection.Right, page.CourseSpec.Direction);
        Assert.AreEqual("外", page.CourseSpec.Layout);
        Assert.AreEqual("芝・右 外", page.CourseSpec.RawLayout);
    }

    [TestMethod]
    public void Parse_コース表記_芝外内はDirectionなしLayoutへ格納する()
    {
        var page = ParseWithMainText("天候 晴 芝 良 2,890メートル（芝 外内）");

        Assert.IsNotNull(page.CourseSpec);
        Assert.AreEqual(2890, page.CourseSpec!.DistanceMeters);
        CollectionAssert.AreEqual(new[] { CourseSurface.Turf }, page.CourseSpec.Surfaces.ToArray());
        Assert.IsNull(page.CourseSpec.Direction);
        Assert.AreEqual("外内", page.CourseSpec.Layout);
    }

    [TestMethod]
    public void Parse_コース表記_障害の芝からダートへの複数surfaceを許容する()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows: [["1", "1", "テストホースA", "騎手A", "3:19.8"]]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良 ダート 良 3,000メートル（芝→ダート）",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月6日 中山 1R", "障害3歳以上未勝利"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var page = (JraRaceResultPage)new RaceResultPageParser().Parse(snapshot);

        Assert.IsNotNull(page.CourseSpec);
        Assert.AreEqual(3000, page.CourseSpec!.DistanceMeters);
        Assert.AreEqual(RaceType.Jump, page.CourseSpec.RaceType);
        CollectionAssert.AreEqual(new[] { CourseSurface.Turf, CourseSurface.Dirt }, page.CourseSpec.Surfaces.ToArray());
    }

    [TestMethod]
    public void Parse_コース表記が存在しない場合はCourseSpecがnullになる()
    {
        var page = ParseWithMainText("天候 晴 芝 良");

        Assert.IsNull(page.CourseSpec);
    }

    [TestMethod]
    public void Parse_コース表記の馬場種別が未知の場合はエラーになる()
    {
        var section = SectionWithMainText("天候 晴 芝 良 1,600メートル（水・左）");
        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var ex = Assert.ThrowsExactly<JraUnexpectedValueException>(
            () => new RaceResultPageParser().Parse(snapshot));

        Assert.AreEqual("Course.Surface", ex.FieldName);
    }

    [TestMethod]
    public void Parse_平地では推定上りダートは平均1Fを分離して解析できる()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム", "推定上り"],
            Rows: [["1", "1", "テストホースA", "騎手A", "1:33.4", "34.5"]]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var page = (JraRaceResultPage)new RaceResultPageParser().Parse(snapshot);

        Assert.AreEqual(34.5m, page.Results[0].EstimatedLast3F);
        Assert.IsNull(page.Results[0].Average1F);
    }

    [TestMethod]
    public void Parse_障害では平均1Fを解析でき推定上りは要求しない()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム", "平均1F"],
            Rows: [["1", "1", "テストホースA", "騎手A", "3:19.8", "13.2"]]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月6日 中山 1R", "障害3歳以上未勝利"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var page = (JraRaceResultPage)new RaceResultPageParser().Parse(snapshot);

        Assert.AreEqual(13.2m, page.Results[0].Average1F);
        Assert.IsNull(page.Results[0].EstimatedLast3F);
    }

    [TestMethod]
    public void Parse_同着を検出できる()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム", "着差"],
            Rows:
            [
                ["1", "1", "テストホースA", "騎手A", "1:33.4", string.Empty],
                ["1", "2", "テストホースB", "騎手B", "1:33.4", "同着"],
            ]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var page = (JraRaceResultPage)new RaceResultPageParser().Parse(snapshot);

        Assert.AreEqual(1, page.Results[0].FinishPosition);
        Assert.AreEqual(1, page.Results[1].FinishPosition);
        Assert.IsTrue(page.Results[1].IsDeadHeat);
        Assert.AreEqual("同着", page.Results[1].MarginRaw);
    }

    [TestMethod]
    public void Parse_降着を検出しFinishPositionとOriginalFinishPositionを分離できる()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows:
            [
                ["10(1位降着)", "1", "テストホースA", "騎手A", "1:33.4"],
                ["1", "2", "テストホースB", "騎手B", "1:33.5"],
            ]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var page = (JraRaceResultPage)new RaceResultPageParser().Parse(snapshot);

        Assert.AreEqual(ResultStatus.Finished, page.Results[0].ResultStatus);
        Assert.AreEqual(10, page.Results[0].FinishPosition);
        Assert.AreEqual(1, page.Results[0].OriginalFinishPosition);

        // 通常完走馬ではOriginalFinishPositionはnull（依頼書18節）。
        Assert.IsNull(page.Results[1].OriginalFinishPosition);
    }

    [TestMethod]
    public void Parse_降着表現を検出したのに元順位を解析できない場合はエラーになる()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows: [["10(降着)", "1", "テストホースA", "騎手A", "1:33.4"]]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var ex = Assert.ThrowsExactly<JraResultConsistencyException>(
            () => new RaceResultPageParser().Parse(snapshot));

        Assert.AreEqual("OriginalFinishPosition", ex.FieldName);
    }

    [TestMethod]
    public void Parse_通常完走の2着以下で着差列が存在するのに空の場合はエラーになる()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム", "着差"],
            Rows:
            [
                ["1", "1", "テストホースA", "騎手A", "1:33.4", string.Empty],
                ["2", "2", "テストホースB", "騎手B", "1:33.6", string.Empty],
            ]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var ex = Assert.ThrowsExactly<JraResultConsistencyException>(
            () => new RaceResultPageParser().Parse(snapshot));

        Assert.AreEqual("MarginRaw", ex.FieldName);
    }

    [TestMethod]
    public void Parse_馬体重人気斤量調教師枠番を解析できる()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "枠番", "馬番", "馬名", "騎手", "調教師", "斤量", "タイム", "人気", "馬体重"],
            Rows:
            [
                ["1", "1", "1", "テストホースA", "騎手A", "調教師A", "57.0", "1:33.4", "1", "482(0)"],
                ["2", "2", "2", "テストホースB", "騎手B", "調教師B", "55.5", "1:33.6", "3", "494(+2)"],
                ["3", "3", "3", "テストホースC", "騎手C", "調教師C", "54.0", "1:33.9", string.Empty, "400 (初出走)"],
            ]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var page = (JraRaceResultPage)new RaceResultPageParser().Parse(snapshot);

        Assert.AreEqual(1, page.Results[0].FrameNumber);
        Assert.AreEqual("調教師A", page.Results[0].TrainerName);
        Assert.AreEqual(57.0m, page.Results[0].AssignedWeight);
        Assert.AreEqual(1, page.Results[0].Popularity);
        Assert.AreEqual(482, page.Results[0].BodyWeight);
        Assert.AreEqual(0, page.Results[0].BodyWeightChange);

        Assert.AreEqual(494, page.Results[1].BodyWeight);
        Assert.AreEqual(2, page.Results[1].BodyWeightChange);

        // Popularityなし・BodyWeightChangeなしは正常な欠損（依頼書24・25節）。
        Assert.IsNull(page.Results[2].Popularity);
        Assert.AreEqual(400, page.Results[2].BodyWeight);
        Assert.IsNull(page.Results[2].BodyWeightChange);
    }

    [TestMethod]
    public void Parse_人気が0の場合はエラーになる()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム", "人気"],
            Rows: [["1", "1", "テストホースA", "騎手A", "1:33.4", "0"]]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var ex = Assert.ThrowsExactly<JraValueParseException>(
            () => new RaceResultPageParser().Parse(snapshot));

        Assert.AreEqual("Popularity", ex.FieldName);
    }

    [TestMethod]
    public void Parse_馬体重が値ありで解析不能な場合はエラーになる()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム", "馬体重"],
            Rows: [["1", "1", "テストホースA", "騎手A", "1:33.4", "計不"]]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var ex = Assert.ThrowsExactly<JraValueParseException>(
            () => new RaceResultPageParser().Parse(snapshot));

        Assert.AreEqual("BodyWeight", ex.FieldName);
    }

    [TestMethod]
    public void Parse_未知の券種はエラーになる()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows: [["1", "1", "テストホースA", "騎手A", "1:33.4"]]);

        // Phase8でワイド・枠連・3連複は既知の券種として対応済みのため、
        // ここでは引き続き既知集合に含まれない架空の券種名を用いる。
        var payoutTable = new PageTableSnapshot(
            Headers: ["式別", "組合せ", "払戻金"],
            Rows: [["馬複", "1-3", "250円"]]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table, payoutTable],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var ex = Assert.ThrowsExactly<JraUnexpectedValueException>(
            () => new RaceResultPageParser().Parse(snapshot));

        Assert.AreEqual("PayoutType", ex.FieldName);
        Assert.AreEqual("馬複", ex.RawValue);
    }

    [TestMethod]
    public void Parse_払戻値が存在するのに解析不能な場合はエラーになる()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows: [["1", "1", "テストホースA", "騎手A", "1:33.4"]]);

        var payoutTable = new PageTableSnapshot(
            Headers: ["式別", "組合せ", "払戻金"],
            Rows: [["単勝", "1", "不明"]]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table, payoutTable],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var ex = Assert.ThrowsExactly<JraValueParseException>(
            () => new RaceResultPageParser().Parse(snapshot));

        Assert.AreEqual("Payout.Amount", ex.FieldName);
    }

    [TestMethod]
    public void Parse_複数の特殊状態と同着降着が混在しても正常に解析できる()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム", "着差"],
            Rows:
            [
                ["10(1位降着)", "1", "テストホースA", "騎手A", "1:33.4", "大差"],
                ["2", "2", "テストホースB", "騎手B", "1:33.5", "同着"],
                ["2", "3", "テストホースC", "騎手C", "1:33.5", "同着"],
                ["取消", "4", "テストホースD", "騎手D", string.Empty, string.Empty],
                ["除外", "5", "テストホースE", "騎手E", string.Empty, string.Empty],
                ["中止", "6", "テストホースF", "騎手F", string.Empty, string.Empty],
                ["失格", "7", "テストホースG", "騎手G", "1:35.0", string.Empty],
            ]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var page = (JraRaceResultPage)new RaceResultPageParser().Parse(snapshot);

        Assert.AreEqual(7, page.Results.Count);
        Assert.AreEqual(10, page.Results[0].FinishPosition);
        Assert.AreEqual(1, page.Results[0].OriginalFinishPosition);
        Assert.AreEqual(2, page.Results[1].FinishPosition);
        Assert.IsTrue(page.Results[1].IsDeadHeat);
        Assert.AreEqual(2, page.Results[2].FinishPosition);
        Assert.IsTrue(page.Results[2].IsDeadHeat);
        Assert.AreEqual(ResultStatus.Cancelled, page.Results[3].ResultStatus);
        Assert.AreEqual(ResultStatus.Excluded, page.Results[4].ResultStatus);
        Assert.AreEqual(ResultStatus.DidNotFinish, page.Results[5].ResultStatus);
        Assert.AreEqual(ResultStatus.Disqualified, page.Results[6].ResultStatus);
    }

    // --- Phase 4: 依頼書32節のテスト方針網羅（通常系・欠損正常系・DOM耐性） ---

    [TestMethod]
    public void Parse_通常のダートレースを解析できる()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows:
            [
                ["1", "1", "テストホースA", "騎手A", "1:24.5"],
                ["2", "2", "テストホースB", "騎手B", "1:24.8"],
            ]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 ダート 良 1,400メートル（ダート・左）",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 東京 3R", "3歳未勝利"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var page = (JraRaceResultPage)new RaceResultPageParser().Parse(snapshot);

        Assert.AreEqual(RaceCourse.Tokyo, page.RaceId.Course);
        Assert.IsNotNull(page.TrackConditionText);
        StringAssert.Contains(page.TrackConditionText!, "ダート:良");
        Assert.AreEqual(2, page.Results.Count);
        Assert.IsNotNull(page.CourseSpec);
        CollectionAssert.AreEqual(new[] { CourseSurface.Dirt }, page.CourseSpec!.Surfaces.ToArray());
        Assert.AreEqual(RaceType.Flat, page.CourseSpec.RaceType);
    }

    [TestMethod]
    public void Parse_障害芝単独レースを解析できる()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows: [["1", "1", "テストホースA", "騎手A", "3:19.8"]]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良 2,890メートル（芝 外内）",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月6日 中山 1R", "障害3歳以上未勝利"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var page = (JraRaceResultPage)new RaceResultPageParser().Parse(snapshot);

        Assert.IsNotNull(page.CourseSpec);
        Assert.AreEqual(RaceType.Jump, page.CourseSpec!.RaceType);
        CollectionAssert.AreEqual(new[] { CourseSurface.Turf }, page.CourseSpec.Surfaces.ToArray());
    }

    [TestMethod]
    public void Parse_古い年代の簡略な列構成でも正常に解析できる()
    {
        // 古い年代のページを想定し、RaceCourseSpec等の新しいフィールドの元になる
        // 表記（メートル表記・性齢・馬体重・人気列等）が一切存在しない簡略な
        // ページでも、既存の必須列（着順・馬番・馬名・騎手・タイム）だけで
        // 正常にParseできることを確認する。
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows:
            [
                ["1", "1", "テストホースA", "騎手A", "1:33.4"],
                ["2", "2", "テストホースB", "騎手B", "1:33.6"],
            ]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "1990年5月5日 中山 11R", "皐月賞"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var page = (JraRaceResultPage)new RaceResultPageParser().Parse(snapshot);

        Assert.AreEqual(2, page.Results.Count);
        Assert.IsNull(page.CourseSpec);
        Assert.IsNull(page.Results[0].Sex);
        Assert.IsNull(page.Results[0].Age);
        Assert.IsNull(page.Results[0].BodyWeight);
        Assert.IsNull(page.Results[0].Popularity);
        Assert.IsNull(page.Results[0].FrameNumber);
        Assert.IsNull(page.Results[0].TrainerName);
    }

    [TestMethod]
    public void Parse_古い年代で一部の払戻券種のみ存在する場合も正常に解析できる()
    {
        // 古い年代等で馬単・三連単が発売されていないレースを想定し、
        // 単勝・複勝のみの払戻テーブルでも正常に解析できることを確認する
        // （依頼書13・28節: 全券種が存在することを要求しない）。
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows: [["1", "1", "テストホースA", "騎手A", "1:33.4"]]);

        var payoutTable = new PageTableSnapshot(
            Headers: ["式別", "組合せ", "払戻金"],
            Rows:
            [
                ["単勝", "1", "250円"],
                ["複勝", "1", "120円"],
            ]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table, payoutTable],
            headings: ["JRA 日本中央競馬会", "1990年5月5日 中山 11R", "皐月賞"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var page = (JraRaceResultPage)new RaceResultPageParser().Parse(snapshot);

        Assert.IsNotNull(page.Payouts);
        Assert.AreEqual(1, page.Payouts!.WinPayouts.Count);
        Assert.AreEqual(1, page.Payouts.PlacePayouts.Count);
        Assert.AreEqual(0, page.Payouts.QuinellaPayouts.Count);
        Assert.AreEqual(0, page.Payouts.ExactaPayouts.Count);
        Assert.AreEqual(0, page.Payouts.TrifectaPayouts.Count);
    }

    [TestMethod]
    public void Parse_必須テーブルが存在しない場合は例外を投げる()
    {
        var section = new PageSectionSnapshot(
            title: "無関係ページ",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var ex = Assert.ThrowsExactly<JraPageParseException>(
            () => new RaceResultPageParser().Parse(snapshot));

        StringAssert.Contains(ex.Message, "テーブル");
    }

    [TestMethod]
    public void Parse_結果行が0件の場合はJraPageStructureExceptionを投げる()
    {
        // 依頼書29節「結果行が1件以上存在する」というRaceResult全体Validationを
        // 検証する。着順テーブル自体は見つかったが結果行が0件（見出し行のみ）の
        // 場合、正常な空結果として扱わずParser異常として検知する。
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows: []);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var ex = Assert.ThrowsExactly<JraPageStructureException>(
            () => new RaceResultPageParser().Parse(snapshot));

        Assert.AreEqual("Results", ex.FieldName);
    }

    [TestMethod]
    public void Parse_HorseNumberが重複する場合はJraResultConsistencyExceptionを投げる()
    {
        // 依頼書14・29節「HorseNumberがレース内で一意」というValidation。
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows:
            [
                ["1", "1", "テストホースA", "騎手A", "1:33.4"],
                ["2", "1", "テストホースB", "騎手B", "1:33.6"],
            ]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var ex = Assert.ThrowsExactly<JraResultConsistencyException>(
            () => new RaceResultPageParser().Parse(snapshot));

        Assert.AreEqual("HorseNumber", ex.FieldName);
        Assert.AreEqual("1", ex.RawValue);
    }

    [TestMethod]
    public void Parse_Finishedでタイム列に値があるのに解析不能な場合はエラーになる()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows: [["1", "1", "テストホースA", "騎手A", "計時不能"]]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var ex = Assert.ThrowsExactly<JraValueParseException>(
            () => new RaceResultPageParser().Parse(snapshot));

        Assert.AreEqual("Time", ex.FieldName);
        Assert.AreEqual("計時不能", ex.RawValue);
    }

    [TestMethod]
    public void Parse_馬番が解析不能な行はJraValueParseExceptionを投げる()
    {
        // 依頼書7・29節「既知項目だが形式不正→Error」。馬番セルから数字を
        // 抽出できない結果行を静かに読み飛ばさず、Parser異常として検知する。
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows:
            [
                ["1", "不明", "テストホースA", "騎手A", "1:33.4"],
                ["2", "2", "テストホースB", "騎手B", "1:33.6"],
            ]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var ex = Assert.ThrowsExactly<JraValueParseException>(
            () => new RaceResultPageParser().Parse(snapshot));

        Assert.AreEqual("HorseNumber", ex.FieldName);
        Assert.AreEqual("不明", ex.RawValue);
    }

    [TestMethod]
    public void Parse_馬名が空欄の結果行はJraValueParseExceptionを投げる()
    {
        // 依頼書14・29節「HorseName: 必須、trim後非空」。馬番は正常にParseできて
        // いるのに馬名列だけが空/空白の結果行を静かに読み飛ばさず、Parser異常
        // として検知する（Phase 7 A1）。見出し行フィルタ・着順欄空白行フィルタは
        // この行より前に確定しているため、誤って正常な非結果行を巻き込まない。
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows:
            [
                ["1", "1", "   ", "騎手A", "1:33.4"],
                ["2", "2", "テストホースB", "騎手B", "1:33.6"],
            ]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var ex = Assert.ThrowsExactly<JraValueParseException>(
            () => new RaceResultPageParser().Parse(snapshot));

        Assert.AreEqual("HorseName", ex.FieldName);
        Assert.AreEqual("   ", ex.RawValue);
    }

    [TestMethod]
    public void Parse_FinishedなのにTimeが完全に欠落している場合はJraResultConsistencyExceptionを投げる()
    {
        // 依頼書19・29節「Finished: Timeあり」。タイム列自体が存在しない
        // （見出しなし）ケースでFinishedにTimeが欠落する場合をエラーにする。
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手"],
            Rows: [["1", "1", "テストホースA", "騎手A"]]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [table],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var ex = Assert.ThrowsExactly<JraResultConsistencyException>(
            () => new RaceResultPageParser().Parse(snapshot));

        Assert.AreEqual("Time", ex.FieldName);
    }

    [TestMethod]
    public void Parse_DOM上のテーブルと見出しの並び順が変わっても解析結果は変わらない()
    {
        // Parserはテーブル・見出しをすべて位置に依存しない検索（見出し文字列の
        // 内容一致）で探索する設計のため、払戻テーブルを着順テーブルより前に
        // 置く・見出しの並びを変えるといったDOM位置変更があっても解析結果が
        // 変わらないことを確認する（依頼書32節「DOM位置変更を模したFixture」）。
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows: [["1", "1", "テストホースA", "騎手A", "1:33.4"]]);

        var payoutTable = new PageTableSnapshot(
            Headers: ["式別", "組合せ", "払戻金"],
            Rows: [["単勝", "1", "250円"]]);

        // 払戻テーブルを結果テーブルより先に置く。見出しの並びにも
        // 無関係な要素を挟む。
        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: "天候 晴 芝 良",
            links: [],
            actions: [],
            tables: [payoutTable, table],
            headings: ["JRA 日本中央競馬会", "勝馬の紹介", "2026年9月5日 中山 11R", "テストステークス", "払戻金"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var page = (JraRaceResultPage)new RaceResultPageParser().Parse(snapshot);

        Assert.AreEqual("テストステークス", page.RaceName);
        Assert.AreEqual(1, page.Results.Count);
        Assert.AreEqual("テストホースA", page.Results[0].HorseName);
        Assert.IsNotNull(page.Payouts);
        Assert.AreEqual(1, page.Payouts!.WinPayouts.Count);
    }

    // Phase8: 2026-09-06 中山1R（実サイトのスクリーンショット書き起こしに基づく
    // 実データFixture）。障害レース、払戻グリッドレイアウト（枠連は空欄）、
    // 着順テーブル内インライン「コーナー通過順位」列、行ラベル形式の下方
    // コーナー通過順位集計テーブル、本賞金・上り集計テキストを含む。
    [TestMethod]
    public void Parse_実サイト書き起こしFixture_全項目を正しく解析する()
    {
        var table = new PageTableSnapshot(
            Headers:
            [
                "着順", "枠", "馬番", "馬名", "性齢", "負担重量", "騎手名", "タイム", "着差",
                "コーナー通過順位", "平均1F", "馬体重(増減)", "調教師名", "単勝人気",
            ],
            Rows:
            [
                ["1", "2", "2", "エーオーキング", "牡4", "60.0", "草野太郎", "3:19.8", "", "4 3 3 2", "13.9", "456(+6)", "久保田貴士", "3"],
                ["2", "7", "7", "ベラトール", "牡4", "60.0", "大江原壱", "3:20.0", "1 1/4", "1 1 1 1", "13.9", "508(+6)", "西田雄一郎", "2"],
            ]);

        var cornerTable = new PageTableSnapshot(
            Headers: ["コーナー通過順位", ""],
            Rows:
            [
                ["1コーナー", "7=3-6,2,1-5,4"],
                ["2コーナー(2周目)", "7=3(6,2)-(1,5)4"],
                ["3コーナー(2周目)", "7-3(6,2)-5=(1,4)"],
                ["4コーナー(2周目)", "7-(6,2)-3-5=1=4"],
            ]);

        var mainText =
            "天候 雨 芝 稍重 ダート 重 " +
            "障害3歳以上 未勝利（混合） 定量 コース：2,880メートル（芝→ダート） " +
            "本賞金(万円) 1着 840 2着 340 3着 210 4着 130 5着 84 " +
            "タイム 上り: 1マイル1分48秒1 4F 51.3 - 3F 38.5 " +
            "単勝 2 400円 (3人気) 枠連 馬連 2-7 810円 (4人気) " +
            "複勝 2 140円 (1人気) 7 190円 (3人気) ワイド 2-7 330円 (5人気) 2-6 360円 (6人気) 6-7 300円 (4人気) 馬単 2-7 1,650円 (9人気) " +
            "3連複 2-6-7 1,160円 (5人気) 3連単 2-7-6 5,650円 (25人気)";

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: mainText,
            links: [],
            actions: [],
            tables: [table, cornerTable],
            headings: ["JRA 日本中央競馬会", "2026年9月6日（日曜）4回中山2日 1レース", "障害3歳以上未勝利"]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var page = (JraRaceResultPage)new RaceResultPageParser().Parse(snapshot);

        // コースは既存のカンマ入り数字対応で解析できる（2,880メートル）。
        Assert.IsNotNull(page.CourseSpec);
        Assert.AreEqual(2880, page.CourseSpec!.DistanceMeters);

        // 着順テーブル内インラインのコーナー通過順位列。
        CollectionAssert.AreEqual(new[] { 4, 3, 3, 2 }, page.Results[0].CornerOrders!.ToArray());
        CollectionAssert.AreEqual(new[] { 1, 1, 1, 1 }, page.Results[1].CornerOrders!.ToArray());
        Assert.IsNull(page.Results[0].EstimatedLast3F);
        Assert.AreEqual(13.9m, page.Results[0].Average1F);

        // 下方集計テーブル（行ラベル形式）。
        Assert.IsNotNull(page.CornerPassages);
        Assert.AreEqual(4, page.CornerPassages!.Count);
        Assert.AreEqual(1, page.CornerPassages[0].CornerNumber);
        Assert.AreEqual("7=3-6,2,1-5,4", page.CornerPassages[0].OrderRaw);
        Assert.AreEqual(4, page.CornerPassages[3].CornerNumber);
        Assert.AreEqual("7-(6,2)-3-5=1=4", page.CornerPassages[3].OrderRaw);

        // 本賞金（万円→円換算）。
        Assert.IsNotNull(page.PrizeMoneyByPosition);
        Assert.AreEqual(8_400_000m, page.PrizeMoneyByPosition![1]);
        Assert.AreEqual(3_400_000m, page.PrizeMoneyByPosition[2]);
        Assert.AreEqual(840_000m, page.PrizeMoneyByPosition[5]);

        // 上り集計（生文字列のまま）。
        Assert.IsNotNull(page.OverallPaceText);
        StringAssert.Contains(page.OverallPaceText, "1マイル1分48秒1");

        // 払戻金（グリッドレイアウト、8券種）。
        Assert.IsNotNull(page.Payouts);
        var payouts = page.Payouts!;

        Assert.AreEqual(1, payouts.WinPayouts.Count);
        Assert.AreEqual("2", payouts.WinPayouts[0].Combination);
        Assert.AreEqual(400m, payouts.WinPayouts[0].Amount);
        Assert.AreEqual(3, payouts.WinPayouts[0].Popularity);

        Assert.AreEqual(2, payouts.PlacePayouts.Count);
        Assert.AreEqual("2", payouts.PlacePayouts[0].Combination);
        Assert.AreEqual(140m, payouts.PlacePayouts[0].Amount);
        Assert.AreEqual("7", payouts.PlacePayouts[1].Combination);
        Assert.AreEqual(190m, payouts.PlacePayouts[1].Amount);

        // 枠連はこのレースではデータなし（空欄）でも正常。
        Assert.AreEqual(0, payouts.BracketQuinellaPayoutsOrEmpty.Count);

        Assert.AreEqual(1, payouts.QuinellaPayouts.Count);
        Assert.AreEqual("2-7", payouts.QuinellaPayouts[0].Combination);
        Assert.AreEqual(810m, payouts.QuinellaPayouts[0].Amount);

        Assert.AreEqual(3, payouts.WidePayoutsOrEmpty.Count);
        Assert.AreEqual("2-7", payouts.WidePayoutsOrEmpty[0].Combination);
        Assert.AreEqual(330m, payouts.WidePayoutsOrEmpty[0].Amount);

        Assert.AreEqual(1, payouts.ExactaPayouts.Count);
        Assert.AreEqual("2-7", payouts.ExactaPayouts[0].Combination);
        Assert.AreEqual(1650m, payouts.ExactaPayouts[0].Amount);
        Assert.AreEqual(9, payouts.ExactaPayouts[0].Popularity);

        Assert.AreEqual(1, payouts.TrioPayoutsOrEmpty.Count);
        Assert.AreEqual("2-6-7", payouts.TrioPayoutsOrEmpty[0].Combination);
        Assert.AreEqual(1160m, payouts.TrioPayoutsOrEmpty[0].Amount);

        Assert.AreEqual(1, payouts.TrifectaPayouts.Count);
        Assert.AreEqual("2-7-6", payouts.TrifectaPayouts[0].Combination);
        Assert.AreEqual(5650m, payouts.TrifectaPayouts[0].Amount);
        Assert.AreEqual(25, payouts.TrifectaPayouts[0].Popularity);
    }

    // Phase8追加指示: 依頼書32節でいう「通常の障害芝→ダートレース」の実データ版
    // 決定版Fixture。2026年9月6日 中山1R 障害3歳以上未勝利の実データ書き起こしを
    // 1着エーオーキング〜7着シグムンドまで7頭すべて再現し、Parse結果全体
    // （着順・馬体重・人気・コーナー通過順位・払戻・本賞金）を1つの統合テストで検証する。
    [TestMethod]
    public void Parse_実サイト書き起こしFixture_7頭分の統合テスト()
    {
        var table = new PageTableSnapshot(
            Headers:
            [
                "着順", "枠", "馬番", "馬名", "性齢", "負担重量", "騎手名", "タイム", "着差",
                "コーナー通過順位", "平均1F", "馬体重(増減)", "調教師名", "単勝人気",
            ],
            Rows:
            [
                ["1", "2", "2", "エーオーキング", "牡4", "60.0", "草野太郎", "3:19.8", "", "4 3 3 2", "13.9", "456(+6)", "久保田貴士", "3"],
                ["2", "7", "7", "ベラトール", "牡4", "60.0", "大江原壱", "3:20.0", "1 1/4", "1 1 1 1", "13.9", "508(+6)", "西田雄一郎", "2"],
                ["3", "6", "6", "エンジョイドライブ", "牡4", "60.0", "小野寺祐太", "3:20.5", "3", "3 3 3 2", "13.9", "476(+4)", "宮本博", "4"],
                ["4", "3", "3", "グロッシェン", "牝5", "60.0", "上野翔", "3:21.1", "3 1/2", "2 2 2 4", "14.0", "476(0)", "手塚貴徳", "1"],
                ["5", "1", "1", "ヴェストラン", "牡5", "60.0", "中村将2", "3:22.1", "6", "5 5 6 6", "14.0", "448(0)", "本田優", "6"],
                ["6", "5", "5", "スラッシュ", "牝3", "58.0", "難波剛健", "3:23.3", "7", "6 5 5 5", "14.1", "470(+2)", "安藤昭夫", "7"],
                ["7", "4", "4", "シグムンド", "牡5", "60.0", "伴啓太", "3:25.4", "大差", "7 7 6 7", "14.3", "510(0)", "岩戸孝樹", "5"],
            ]);

        var cornerTable = new PageTableSnapshot(
            Headers: ["コーナー通過順位", ""],
            Rows:
            [
                ["1コーナー", "7=3-6,2,1-5,4"],
                ["2コーナー(2周目)", "7=3(6,2)-(1,5)4"],
                ["3コーナー(2周目)", "7-3(6,2)-5=(1,4)"],
                ["4コーナー(2周目)", "7-(6,2)-3-5=1=4"],
            ]);

        var mainText =
            "発走時刻：10時05分 天候 雨 芝 稍重 ダート 重 " +
            "障害3歳以上 未勝利（混合） 定量 コース：2,880メートル（芝→ダート） " +
            "本賞金(万円) 1着 840 2着 340 3着 210 4着 130 5着 84 " +
            "タイム 上り: 1マイル1分48秒1 4F 51.3 - 3F 38.5 " +
            "単勝 2 400円 (3人気) 枠連 馬連 2-7 810円 (4人気) " +
            "複勝 2 140円 (1人気) 7 190円 (3人気) ワイド 2-7 330円 (5人気) 2-6 360円 (6人気) 6-7 300円 (4人気) 馬単 2-7 1,650円 (9人気) " +
            "3連複 2-6-7 1,160円 (5人気) 3連単 2-7-6 5,650円 (25人気)";

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: mainText,
            links: [],
            actions: [],
            tables: [table, cornerTable],
            headings:
            [
                "JRA 日本中央競馬会",
                "2026年9月6日（日曜）4回中山2日 1レース",
                "障害3歳以上未勝利",
            ]);

        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        var page = (JraRaceResultPage)new RaceResultPageParser().Parse(snapshot);

        // --- 基本情報 ---
        Assert.AreEqual(new DateOnly(2026, 9, 6), page.RaceId.Date);
        Assert.AreEqual(RaceCourse.Nakayama, page.RaceId.Course);
        Assert.AreEqual(1, page.RaceId.Number);
        Assert.AreEqual("障害3歳以上未勝利", page.RaceName);
        Assert.AreEqual("雨", page.WeatherText);
        Assert.AreEqual("芝:稍重 ダート:重", page.TrackConditionText);
        Assert.IsNotNull(page.CourseSpec);
        Assert.AreEqual(2880, page.CourseSpec!.DistanceMeters);
        Assert.AreEqual(RaceType.Jump, page.CourseSpec.RaceType);

        // --- 着順テーブル：7頭すべて ---
        Assert.AreEqual(7, page.Results.Count);

        var r1 = page.Results[0];
        Assert.AreEqual(ResultStatus.Finished, r1.ResultStatus);
        Assert.AreEqual(1, r1.FinishPosition);
        Assert.AreEqual(2, r1.HorseNumber);
        Assert.AreEqual(2, r1.FrameNumber);
        Assert.AreEqual("エーオーキング", r1.HorseName);
        Assert.AreEqual(HorseSex.Colt, r1.Sex);
        Assert.AreEqual(4, r1.Age);
        Assert.AreEqual(60.0m, r1.AssignedWeight);
        Assert.AreEqual("草野太郎", r1.JockeyName);
        Assert.AreEqual(new TimeSpan(0, 0, 3, 19, 800), r1.Time);
        Assert.IsNull(r1.MarginRaw);
        CollectionAssert.AreEqual(new[] { 4, 3, 3, 2 }, r1.CornerOrders!.ToArray());
        Assert.IsNull(r1.EstimatedLast3F);
        Assert.AreEqual(13.9m, r1.Average1F);
        Assert.AreEqual(456, r1.BodyWeight);
        Assert.AreEqual(6, r1.BodyWeightChange);
        Assert.AreEqual("久保田貴士", r1.TrainerName);
        Assert.AreEqual(3, r1.Popularity);

        var r2 = page.Results[1];
        Assert.AreEqual(2, r2.FinishPosition);
        Assert.AreEqual(7, r2.HorseNumber);
        Assert.AreEqual("ベラトール", r2.HorseName);
        Assert.AreEqual("1 1/4", r2.MarginRaw);
        CollectionAssert.AreEqual(new[] { 1, 1, 1, 1 }, r2.CornerOrders!.ToArray());
        Assert.AreEqual(508, r2.BodyWeight);
        Assert.AreEqual(6, r2.BodyWeightChange);
        Assert.AreEqual("西田雄一郎", r2.TrainerName);
        Assert.AreEqual(2, r2.Popularity);

        var r3 = page.Results[2];
        Assert.AreEqual(3, r3.FinishPosition);
        Assert.AreEqual(6, r3.HorseNumber);
        Assert.AreEqual("エンジョイドライブ", r3.HorseName);
        Assert.AreEqual("3", r3.MarginRaw);
        CollectionAssert.AreEqual(new[] { 3, 3, 3, 2 }, r3.CornerOrders!.ToArray());
        Assert.AreEqual(476, r3.BodyWeight);
        Assert.AreEqual(4, r3.BodyWeightChange);
        Assert.AreEqual("宮本博", r3.TrainerName);
        Assert.AreEqual(4, r3.Popularity);

        var r4 = page.Results[3];
        Assert.AreEqual(4, r4.FinishPosition);
        Assert.AreEqual(3, r4.HorseNumber);
        Assert.AreEqual("グロッシェン", r4.HorseName);
        Assert.AreEqual(HorseSex.Filly, r4.Sex);
        Assert.AreEqual(5, r4.Age);
        Assert.AreEqual("3 1/2", r4.MarginRaw);
        CollectionAssert.AreEqual(new[] { 2, 2, 2, 4 }, r4.CornerOrders!.ToArray());
        Assert.AreEqual(476, r4.BodyWeight);
        Assert.AreEqual(0, r4.BodyWeightChange);
        Assert.AreEqual("手塚貴徳", r4.TrainerName);
        Assert.AreEqual(1, r4.Popularity);

        var r5 = page.Results[4];
        Assert.AreEqual(5, r5.FinishPosition);
        Assert.AreEqual(1, r5.HorseNumber);
        Assert.AreEqual("ヴェストラン", r5.HorseName);
        Assert.AreEqual("6", r5.MarginRaw);
        CollectionAssert.AreEqual(new[] { 5, 5, 6, 6 }, r5.CornerOrders!.ToArray());
        Assert.AreEqual(448, r5.BodyWeight);
        Assert.AreEqual(0, r5.BodyWeightChange);
        Assert.AreEqual("本田優", r5.TrainerName);
        Assert.AreEqual(6, r5.Popularity);

        var r6 = page.Results[5];
        Assert.AreEqual(6, r6.FinishPosition);
        Assert.AreEqual(5, r6.HorseNumber);
        Assert.AreEqual("スラッシュ", r6.HorseName);
        Assert.AreEqual(HorseSex.Filly, r6.Sex);
        Assert.AreEqual(3, r6.Age);
        Assert.AreEqual(58.0m, r6.AssignedWeight);
        Assert.AreEqual("7", r6.MarginRaw);
        CollectionAssert.AreEqual(new[] { 6, 5, 5, 5 }, r6.CornerOrders!.ToArray());
        Assert.AreEqual(470, r6.BodyWeight);
        Assert.AreEqual(2, r6.BodyWeightChange);
        Assert.AreEqual("安藤昭夫", r6.TrainerName);
        Assert.AreEqual(7, r6.Popularity);

        var r7 = page.Results[6];
        Assert.AreEqual(7, r7.FinishPosition);
        Assert.AreEqual(4, r7.HorseNumber);
        Assert.AreEqual("シグムンド", r7.HorseName);
        Assert.AreEqual("大差", r7.MarginRaw);
        CollectionAssert.AreEqual(new[] { 7, 7, 6, 7 }, r7.CornerOrders!.ToArray());
        Assert.AreEqual(14.3m, r7.Average1F);
        Assert.AreEqual(510, r7.BodyWeight);
        Assert.AreEqual(0, r7.BodyWeightChange);
        Assert.AreEqual("岩戸孝樹", r7.TrainerName);
        Assert.AreEqual(5, r7.Popularity);

        // --- ページ単位のコーナー通過順位集計（行ラベル形式、1〜4コーナー） ---
        Assert.IsNotNull(page.CornerPassages);
        Assert.AreEqual(4, page.CornerPassages!.Count);
        Assert.AreEqual(1, page.CornerPassages[0].CornerNumber);
        Assert.AreEqual("7=3-6,2,1-5,4", page.CornerPassages[0].OrderRaw);
        Assert.AreEqual(2, page.CornerPassages[1].CornerNumber);
        Assert.AreEqual("7=3(6,2)-(1,5)4", page.CornerPassages[1].OrderRaw);
        Assert.AreEqual(3, page.CornerPassages[2].CornerNumber);
        Assert.AreEqual("7-3(6,2)-5=(1,4)", page.CornerPassages[2].OrderRaw);
        Assert.AreEqual(4, page.CornerPassages[3].CornerNumber);
        Assert.AreEqual("7-(6,2)-3-5=1=4", page.CornerPassages[3].OrderRaw);

        // --- 本賞金（万円→円換算、1〜5着） ---
        Assert.IsNotNull(page.PrizeMoneyByPosition);
        Assert.AreEqual(8_400_000m, page.PrizeMoneyByPosition![1]);
        Assert.AreEqual(3_400_000m, page.PrizeMoneyByPosition[2]);
        Assert.AreEqual(2_100_000m, page.PrizeMoneyByPosition[3]);
        Assert.AreEqual(1_300_000m, page.PrizeMoneyByPosition[4]);
        Assert.AreEqual(840_000m, page.PrizeMoneyByPosition[5]);

        // --- 払戻金（グリッドレイアウト、枠連は空欄） ---
        Assert.IsNotNull(page.Payouts);
        var payouts = page.Payouts!;

        Assert.AreEqual(1, payouts.WinPayouts.Count);
        Assert.AreEqual("2", payouts.WinPayouts[0].Combination);
        Assert.AreEqual(400m, payouts.WinPayouts[0].Amount);
        Assert.AreEqual(3, payouts.WinPayouts[0].Popularity);

        Assert.AreEqual(2, payouts.PlacePayouts.Count);
        Assert.AreEqual("2", payouts.PlacePayouts[0].Combination);
        Assert.AreEqual(140m, payouts.PlacePayouts[0].Amount);
        Assert.AreEqual(1, payouts.PlacePayouts[0].Popularity);
        Assert.AreEqual("7", payouts.PlacePayouts[1].Combination);
        Assert.AreEqual(190m, payouts.PlacePayouts[1].Amount);
        Assert.AreEqual(3, payouts.PlacePayouts[1].Popularity);

        Assert.AreEqual(0, payouts.BracketQuinellaPayoutsOrEmpty.Count);

        Assert.AreEqual(1, payouts.QuinellaPayouts.Count);
        Assert.AreEqual("2-7", payouts.QuinellaPayouts[0].Combination);
        Assert.AreEqual(810m, payouts.QuinellaPayouts[0].Amount);
        Assert.AreEqual(4, payouts.QuinellaPayouts[0].Popularity);

        Assert.AreEqual(3, payouts.WidePayoutsOrEmpty.Count);
        Assert.AreEqual("2-7", payouts.WidePayoutsOrEmpty[0].Combination);
        Assert.AreEqual(330m, payouts.WidePayoutsOrEmpty[0].Amount);
        Assert.AreEqual("2-6", payouts.WidePayoutsOrEmpty[1].Combination);
        Assert.AreEqual(360m, payouts.WidePayoutsOrEmpty[1].Amount);
        Assert.AreEqual("6-7", payouts.WidePayoutsOrEmpty[2].Combination);
        Assert.AreEqual(300m, payouts.WidePayoutsOrEmpty[2].Amount);

        Assert.AreEqual(1, payouts.ExactaPayouts.Count);
        Assert.AreEqual("2-7", payouts.ExactaPayouts[0].Combination);
        Assert.AreEqual(1650m, payouts.ExactaPayouts[0].Amount);
        Assert.AreEqual(9, payouts.ExactaPayouts[0].Popularity);

        Assert.AreEqual(1, payouts.TrioPayoutsOrEmpty.Count);
        Assert.AreEqual("2-6-7", payouts.TrioPayoutsOrEmpty[0].Combination);
        Assert.AreEqual(1160m, payouts.TrioPayoutsOrEmpty[0].Amount);
        Assert.AreEqual(5, payouts.TrioPayoutsOrEmpty[0].Popularity);

        Assert.AreEqual(1, payouts.TrifectaPayouts.Count);
        Assert.AreEqual("2-7-6", payouts.TrifectaPayouts[0].Combination);
        Assert.AreEqual(5650m, payouts.TrifectaPayouts[0].Amount);
        Assert.AreEqual(25, payouts.TrifectaPayouts[0].Popularity);

        // --- 上り集計（生文字列） ---
        Assert.IsNotNull(page.OverallPaceText);
        StringAssert.Contains(page.OverallPaceText, "1マイル1分48秒1");
    }

    private static PageSectionSnapshot SectionWithMainText(string mainText)
        => new(
            title: "レース結果",
            mainText: mainText,
            links: [],
            actions: [],
            tables: [new PageTableSnapshot(
                Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
                Rows: [["1", "1", "テストホースA", "騎手A", "1:33.4"]])],
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス"]);

    private static JraRaceResultPage ParseWithMainText(string mainText)
    {
        var section = SectionWithMainText(mainText);
        var snapshot = new PageSnapshot(Url, "レース結果 JRA", [section]);

        return (JraRaceResultPage)new RaceResultPageParser().Parse(snapshot);
    }
}
