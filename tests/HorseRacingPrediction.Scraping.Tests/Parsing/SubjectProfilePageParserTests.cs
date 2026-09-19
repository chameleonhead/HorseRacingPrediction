using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using HorseRacingPrediction.Scraping.Tests.TestSupport;

namespace HorseRacingPrediction.Scraping.Tests.Parsing;

[TestClass]
public sealed class SubjectProfilePageParserTests
{
    [TestMethod]
    public void IdentityNames_RemoveOfficialRegistrationMarksAndAffiliations()
    {
        Assert.AreEqual("アジアエクスプレス",
            SubjectProfilePageParser.CanonicalizeDisplayName("Horse", "マルガイ アジアエクスプレス"));
        Assert.AreEqual("パレスラン",
            SubjectProfilePageParser.CanonicalizeDisplayName("Horse", "マルチ パレスラン"));
        Assert.AreEqual("尾形 和幸",
            SubjectProfilePageParser.CanonicalizeDisplayName("Trainer", "尾形 和幸（美浦）"));
        Assert.AreEqual("識別 騎手",
            SubjectProfilePageParser.CanonicalizeDisplayName("Jockey", "▲識別 騎手（美浦）"));
        Assert.AreEqual(SubjectProfilePageParser.NormalizeIdentityName("Trainer", "尾形 和幸"),
            SubjectProfilePageParser.NormalizeIdentityName("Trainer", "尾形 和幸(美浦)"));
    }

    [TestMethod]
    public void Parse_PreservesAllSeventyRacesAndExcludesNonJraHistory()
    {
        var rows = Enumerable.Range(1, 70).Select(i => (IReadOnlyList<string>)new[] { "2026年9月6日", "中山", "レース" + i }).ToArray();
        var cells = rows.Select((r, i) => (IReadOnlyList<TestPageCell>)new[] {
            new TestPageCell(r[0]), new TestPageCell(r[1]),
            new TestPageCell(r[2], [new("a", [], r[2], "https://www.jra.go.jp/result/" + i)]) }).ToArray();
        var snapshot = new TestPageSnapshot("https://www.jra.go.jp/horse", "競走馬情報", [new(
            "プロフィール", "", [], [],
            [
                new(["項目", "値"], [["生年月日", "2024年4月11日"], ["父", "父馬"], ["毛色", "栗毛"]]),
                new(["年月日", "場", "レース名"], rows, cells),
                new(["年月日", "場", "レース名"], [["2025年3月1日", "海外", "海外競走"]]),
            ],
            ["競走馬情報 テストホースTest Horse（JPN）"])]);
        var page = SubjectProfilePageParser.Parse(snapshot, "Horse");
        Assert.AreEqual("テストホース", page.Profile.Name); Assert.AreEqual("父馬", page.Profile.Fields["父"]);
        Assert.AreEqual(71, page.Races.Count); Assert.AreEqual(70, page.Races.Count(x => x.ExclusionReason is null));
        Assert.IsNotNull(page.Races.Last().ExclusionReason);
        var birthMismatch = Assert.ThrowsExactly<JraSubjectIdentificationException>(() =>
            SubjectProfilePageParser.Validate(page, new("Horse", "テストホース", new(2023, 4, 11))));
        Assert.AreEqual(JraSubjectIdentificationFailureKind.BirthDateMismatch, birthMismatch.Kind);
        Assert.AreEqual("テストホース", birthMismatch.ActualName);
        Assert.AreEqual(snapshot.Url, birthMismatch.FinalUrl);

        var nameMismatch = Assert.ThrowsExactly<JraSubjectIdentificationException>(() =>
            SubjectProfilePageParser.Validate(page, new("Horse", "別馬")));
        Assert.AreEqual(JraSubjectIdentificationFailureKind.ProfileNameMismatch, nameMismatch.Kind);
        Assert.AreEqual("別馬", nameMismatch.ExpectedName);
        Assert.AreEqual("テストホース", nameMismatch.ActualName);

        var sourceMismatch = Assert.ThrowsExactly<JraSubjectIdentificationException>(() =>
            SubjectProfilePageParser.Validate(page,
                new("Horse", "テストホース", SourceIdentity: "https://www.jra.go.jp/expected")));
        Assert.AreEqual(JraSubjectIdentificationFailureKind.SourceIdentityMismatch, sourceMismatch.Kind);
        Assert.AreEqual("https://www.jra.go.jp/expected", sourceMismatch.RequestedUrl);
        Assert.AreEqual(snapshot.Url, sourceMismatch.FinalUrl);
    }

    [TestMethod]
    public void Parse_HorseHeadingRemovesRegistrationMarkBeforeValidation()
    {
        var snapshot = new TestPageSnapshot("https://www.jra.go.jp/horse", "競走馬情報", [new(
            "プロフィール", "", [], [],
            [new(["項目", "値"], [["生年月日", "2011年2月9日"]])],
            ["競走馬情報 マルガイ アジアエクスプレス（USA）"])]);

        var page = SubjectProfilePageParser.Parse(snapshot, "Horse");

        Assert.AreEqual("アジアエクスプレス", page.Profile.Name);
        SubjectProfilePageParser.Validate(page, new("Horse", "アジアエクスプレス"));
    }
}
