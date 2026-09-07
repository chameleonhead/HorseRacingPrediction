using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Parsing;

namespace HorseRacingPrediction.Scraping.Tests.Parsing;

[TestClass]
public sealed class SubjectProfilePageParserTests
{
    [TestMethod]
    public void Parse_PreservesAllSeventyRacesAndExcludesNonJraHistory()
    {
        var rows = Enumerable.Range(1, 70).Select(i => (IReadOnlyList<string>)new[] { "2026年9月6日", "中山", "レース" + i }).ToArray();
        var cells = rows.Select((r, i) => (IReadOnlyList<PageTableCellSnapshot>)new[] {
            new PageTableCellSnapshot(r[0],[]),new PageTableCellSnapshot(r[1],[]),
            new PageTableCellSnapshot(r[2],[new("a",[],r[2],"https://www.jra.go.jp/result/"+i)])}).ToArray();
        var snapshot = new PageSnapshot("https://www.jra.go.jp/horse", "競走馬情報", [new("プロフィール","",[],[],[
            new(["項目","値"],[["生年月日","2024年4月11日"],["父","父馬"],["毛色","栗毛"]]),
            new(["年月日","場","レース名"],rows,cells),new(["年月日","場","レース名"],[["2025年3月1日","海外","海外競走"]])
        ],["競走馬情報 テストホースTest Horse（JPN）"])]);
        var page = SubjectProfilePageParser.Parse(snapshot, "Horse");
        Assert.AreEqual("テストホース", page.Profile.Name); Assert.AreEqual("父馬", page.Profile.Fields["父"]);
        Assert.AreEqual(71, page.Races.Count); Assert.AreEqual(70, page.Races.Count(x => x.ExclusionReason is null));
        Assert.IsNotNull(page.Races.Last().ExclusionReason);
        Assert.Throws<JraCollectionException>(() => SubjectProfilePageParser.Validate(page, new("Horse", "テストホース", new(2023, 4, 11))));
        Assert.Throws<JraCollectionException>(() => SubjectProfilePageParser.Validate(page, new("Horse", "別馬")));
    }
}
