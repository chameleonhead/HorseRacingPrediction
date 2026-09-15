using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Navigation;

namespace HorseRacingPrediction.Scraping.Tests.Navigation;

[TestClass]
public sealed class BoundedHorseCandidateEvidenceTests
{
    [TestMethod]
    public void TryAdd_AllowsThirtyTwoUniqueCandidatesAndRejectsThirtyThird()
    {
        var evidence = new JraNavigator.BoundedHorseCandidateEvidence();

        for (var index = 0; index < JraNavigator.BoundedHorseCandidateEvidence.MaximumCandidates; index++)
            Assert.IsTrue(evidence.TryAdd(new PageLinkSnapshot($"/horse/{index}", $"Horse {index}")));

        Assert.IsFalse(evidence.TryAdd(new PageLinkSnapshot("/horse/overflow", "Overflow")));
        Assert.HasCount(32, evidence.Items);
    }

    [TestMethod]
    public void TryAdd_DuplicateUrlDoesNotConsumeCandidateOrByteBudget()
    {
        var evidence = new JraNavigator.BoundedHorseCandidateEvidence();

        Assert.IsTrue(evidence.TryAdd(new PageLinkSnapshot("/horse/1", "First")));
        Assert.IsTrue(evidence.TryAdd(new PageLinkSnapshot("/horse/1", new string('x', 300_000))));

        Assert.HasCount(1, evidence.Items);
    }

    [TestMethod]
    public void TryAdd_AllowsExactByteBoundaryAndRejectsOverflow()
    {
        var evidence = new JraNavigator.BoundedHorseCandidateEvidence();
        const string url = "/horse/one";
        var title = new string('a', JraNavigator.BoundedHorseCandidateEvidence.MaximumBytes - url.Length);

        Assert.IsTrue(evidence.TryAdd(new PageLinkSnapshot(url, title)));
        Assert.IsFalse(evidence.TryAdd(new PageLinkSnapshot("/horse/two", "b")));
    }
}
