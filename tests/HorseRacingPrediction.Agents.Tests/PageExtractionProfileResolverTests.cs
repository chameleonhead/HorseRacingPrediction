using HorseRacingPrediction.Agents.Agents;

namespace HorseRacingPrediction.Agents.Tests;

[TestClass]
public class PageExtractionProfileResolverTests
{
    [TestMethod]
    public void Resolve_WithTinyModel_ReturnsTinyProfile()
    {
        var profile = PageExtractionProfileResolver.Resolve(modelId: "google/gemma-3n-e2b");

        Assert.AreEqual("tiny", profile.Name);
        Assert.IsFalse(profile.IncludeSnapshotInPrompt);
        Assert.IsLessThan(PageExtractionProfile.Standard.MaxInputLength, profile.MaxInputLength);
    }

    [TestMethod]
    public void Resolve_With7BModel_ReturnsSmallProfile()
    {
        var profile = PageExtractionProfileResolver.Resolve(modelId: "qwen2.5-7b-instruct");

        Assert.AreEqual("small", profile.Name);
        Assert.IsTrue(profile.IncludeSnapshotInPrompt);
        Assert.IsLessThan(PageExtractionProfile.Standard.MaxInputLength, profile.MaxInputLength);
    }

    [TestMethod]
    public void Resolve_WithProfileOverride_ReturnsOverrideProfile()
    {
        var profile = PageExtractionProfileResolver.Resolve(
            modelId: "gpt-4o",
            profileOverride: "tiny");

        Assert.AreEqual("tiny", profile.Name);
    }
}
