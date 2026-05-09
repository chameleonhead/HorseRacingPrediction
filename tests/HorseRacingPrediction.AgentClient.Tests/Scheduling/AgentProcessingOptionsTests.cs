using HorseRacingPrediction.AgentClient.Scheduling;

namespace HorseRacingPrediction.AgentClient.Tests.Scheduling;

[TestClass]
public sealed class AgentProcessingOptionsTests
{
    [TestMethod]
    public void Defaults_AreConfiguredForSeparatedProcessing()
    {
        var options = new AgentProcessingOptions();

        Assert.IsTrue(options.Enabled);
        Assert.AreEqual(180, options.ScrapingIntervalMinutes);
        Assert.AreEqual(60, options.PredictionIntervalMinutes);
        Assert.AreEqual(10, options.PredictionMinAgeMinutes);
        Assert.AreEqual(20, options.PredictionBatchSize);
        Assert.AreEqual(2, options.ResultLookbackDays);
        Assert.AreEqual(0, options.ResultLookaheadDays);
        Assert.IsTrue(options.EnableScheduleCollection);
        Assert.AreEqual(14, options.ScheduleLookaheadDays);
        Assert.IsTrue(options.EnableTextInsightCollection);
        Assert.HasCount(3, options.TextInsightQueryTemplates);
    }
}