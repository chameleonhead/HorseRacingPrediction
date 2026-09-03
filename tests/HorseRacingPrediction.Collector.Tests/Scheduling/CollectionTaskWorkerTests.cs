// JRAサイト再設計（docs/jra-scraping.md）により、対象の CollectionTaskWorker は一時的に無効化されている。
#if false
using HorseRacingPrediction.Collector.Scheduling;

namespace HorseRacingPrediction.Collector.Tests.Scheduling;

[TestClass]
public sealed class CollectionTaskWorkerTests
{
    [TestMethod]
    public void ReadLambdaNotification_ParsesSingleSqsRecord()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path,
                """
                {"Records":[{"body":"{\"taskId\":\"task-1\",\"jobType\":\"RaceCardCollection\",\"deduplicationKey\":\"key-1\"}"}]}
                """);

            var notification = CollectionTaskWorker.ReadLambdaNotification(path);

            Assert.IsNotNull(notification);
            Assert.AreEqual("task-1", notification.TaskId);
            Assert.AreEqual("RaceCardCollection", notification.JobType);
            Assert.AreEqual("key-1", notification.DeduplicationKey);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
#endif
