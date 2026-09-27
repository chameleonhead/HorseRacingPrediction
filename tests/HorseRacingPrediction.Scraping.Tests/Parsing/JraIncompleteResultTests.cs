using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using HorseRacingPrediction.Scraping.Tests.TestSupport;

namespace HorseRacingPrediction.Scraping.Tests.Parsing;

[TestClass]
public sealed class JraIncompleteResultTests
{
    private const string Url = "https://www.jra.go.jp/JRADB/accessS.html?CNAME=sample";
    private static readonly RaceId Race = new(new DateOnly(2026, 9, 27), RaceCourse.Nakayama, 1);

    [TestMethod]
    public void Parse_FinishedNumericRowWithExistingEmptyTime_ThrowsBoundedTypedEvidence()
    {
        var snapshot = BuildSnapshot(["1", "3", "テストホース", "騎手", ""], [
            new TestPageCell("1"), new TestPageCell("3"), new TestPageCell("テストホース"),
            new TestPageCell("騎手"), new TestPageCell("")]);

        var exception = Assert.ThrowsExactly<JraIncompleteResultException>(
            () => new RaceResultPageParser().Parse(snapshot));

        Assert.AreEqual(Race, exception.RaceId);
        Assert.AreEqual(3, exception.HorseNumber);
        Assert.AreEqual("Time", exception.FieldName);
        Assert.AreEqual(string.Empty, exception.RawValue);
        Assert.HasCount(5, exception.Headers);
        Assert.HasCount(5, exception.Cells);
        Assert.AreEqual("", exception.Cells[^1]);
        Assert.IsTrue(exception.Message.Contains("馬番3", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Parse_FinishedNumericRowWithMissingTimeColumn_RemainsStructuralFailure()
    {
        var snapshot = BuildSnapshot(["1", "3", "テストホース", "騎手"], [
            new TestPageCell("1"), new TestPageCell("3"), new TestPageCell("テストホース"),
            new TestPageCell("騎手")]);

        var exception = Assert.ThrowsExactly<JraResultConsistencyException>(
            () => new RaceResultPageParser().Parse(snapshot));

        Assert.IsNotInstanceOfType(exception, typeof(JraIncompleteResultException));
        Assert.AreEqual("Time", exception.FieldName);
    }

    [TestMethod]
    public void Parse_FinishedNumericRowWithNonEmptyMalformedTime_RemainsValueFailure()
    {
        var snapshot = BuildSnapshot(["1", "3", "テストホース", "騎手", "未確定"], [
            new TestPageCell("1"), new TestPageCell("3"), new TestPageCell("テストホース"),
            new TestPageCell("騎手"), new TestPageCell("未確定")]);

        var exception = Assert.ThrowsExactly<JraValueParseException>(
            () => new RaceResultPageParser().Parse(snapshot));

        Assert.IsNotInstanceOfType(exception, typeof(JraIncompleteResultException));
        Assert.AreEqual("Time", exception.FieldName);
        Assert.AreEqual("未確定", exception.RawValue);
    }

    private static TestPageSnapshot BuildSnapshot(
        IReadOnlyList<string> headers, IReadOnlyList<TestPageCell> cells)
    {
        var actualHeaders = headers.Count == 5
            ? new[] { "着順", "馬番", "馬名", "騎手", "タイム" }
            : new[] { "着順", "馬番", "馬名", "騎手" };
        var table = new TestPageTable(actualHeaders, [cells.Select(cell => cell.Text).ToArray()], [cells]);
        var section = new TestPageSection("レース結果", "天候 晴 芝 良", [], [], [table],
            ["2026年9月27日 中山 1R", "テストステークス"]);
        return new TestPageSnapshot(Url, "2026年9月27日 中山 1R 結果", [section]);
    }
}
