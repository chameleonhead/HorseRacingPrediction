using HorseRacingPrediction.ApiClient;

namespace HorseRacingPrediction.Collector.Tests.Http;

[TestClass]
public sealed class JockeyNameNormalizerTests
{
    [TestMethod]
    [DataRow("▲森田 誠也")]
    [DataRow("△ 森田 誠也")]
    [DataRow("☆森田 誠也")]
    [DataRow("★森田 誠也")]
    [DataRow("◇森田 誠也")]
    [DataRow("▽森田 誠也")]
    [DataRow(" ▲ △ 森田 誠也 ")]
    public void Normalize_RemovesAllLeadingAllowanceMarks(string source)
    {
        Assert.AreEqual("森田 誠也", JockeyNameNormalizer.Normalize(source));
    }

    [TestMethod]
    public void Normalize_UnmarkedName_IsUnchanged()
    {
        Assert.AreEqual("森田 誠也", JockeyNameNormalizer.Normalize(" 森田 誠也 "));
    }

    [TestMethod]
    public void Normalize_MarkInsideName_IsPreserved()
    {
        Assert.AreEqual("森田▲誠也", JockeyNameNormalizer.Normalize("森田▲誠也"));
    }

    [TestMethod]
    public void Normalize_BeforeIdGeneration_MarkedAndUnmarkedNamesProduceSameId()
    {
        var marked = DeterministicIdGenerator.BuildEntityId(
            "jockey",
            DeterministicIdGenerator.NormalizeDisplayName(JockeyNameNormalizer.Normalize("▲森田誠也")));
        var unmarked = DeterministicIdGenerator.BuildEntityId(
            "jockey",
            DeterministicIdGenerator.NormalizeDisplayName(JockeyNameNormalizer.Normalize("森田誠也")));

        Assert.AreEqual(unmarked, marked);
    }
}
