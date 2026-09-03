using HorseRacingPrediction.Api.Notifications;
using HorseRacingPrediction.Collector.Scheduling;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class SnsJobFailureNotificationPublisherTests
{
    [TestMethod]
    public void BuildSmsMessage_IncludesStatusTypeAndEncodedJobLink()
    {
        var notification = new PendingJobFailureNotification(
            "notification-id",
            "RaceCardCollection:2026/08/28 東京",
            "RaceCardCollection",
            "2026/08/28 東京",
            "Failed",
            "browser error",
            3,
            DateTimeOffset.UtcNow,
            0);

        var message = SnsJobFailureNotificationPublisher.BuildSmsMessage(
            "https://100-49-86-109.sslip.io/",
            notification);

        Assert.AreEqual(
            "HRP Failed RaceCardCollection\n" +
            "https://100-49-86-109.sslip.io/jobs/RaceCardCollection%3A2026%2F08%2F28%20%E6%9D%B1%E4%BA%AC",
            message);
        Assert.IsFalse(message.Contains("browser error", StringComparison.Ordinal));
    }
}
