using HorseRacingPrediction.Collector.Scheduling;
using HorseRacingPrediction.Scraping.Scrapers.Jra;

namespace HorseRacingPrediction.Collector.Tests.Scheduling;

[TestClass]
public sealed class ResultDayChildTaskFactoryTests
{
    [TestMethod]
    public void Create_ThirtySixUrls_CreatesThirtySixDistinctRaceTasks()
    {
        var date = new DateOnly(2026, 8, 15);
        var courses = new[] { "札幌", "新潟", "中京" };
        var urls = courses.SelectMany(course => Enumerable.Range(1, 12)
            .Select(number => new JraRaceResultUrl($"https://example.test/{course}/{number}", course, null, date, number)))
            .ToList();
        var parent = new ResultDayCollectionRequestPayload(date, "JRA", urls, urls.Count);

        var children = ResultDayChildTaskFactory.Create(parent);

        Assert.HasCount(36, children);
        Assert.AreEqual(36, children.Select(x => x.DeduplicationKey).Distinct(StringComparer.Ordinal).Count());
    }
}
