using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Scraping.Tests.TestSupport;

internal static class PublishedRaceCardMeeting
{
    public static async Task<JraRaceListPage> FindAsync(JraSession session, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var candidates = new List<(DateOnly Date, RaceCourse Course)>();
        // 月初・月末でも探索期間内の開催を落とさない。
        var months = new[] { today.AddDays(-5), today, today.AddDays(7) }
            .Select(date => new YearMonth(date.Year, date.Month)).Distinct();
        foreach (var month in months)
        {
            var page = await session.Navigate.ToCalendarAsync(month, cancellationToken);
            Assert.IsInstanceOfType<JraCalendarPage>(page);
            candidates.AddRange(((JraCalendarPage)page).RaceDates
                .Where(meeting => meeting.Date <= today.AddDays(7)
                    && session.Navigate.IsWithinRaceCardLookupPeriod(meeting.Date))
                .SelectMany(meeting => meeting.Courses.Select(course => (meeting.Date, course))));
        }

        foreach (var candidate in candidates.Distinct().OrderBy(x => Math.Abs(x.Date.DayNumber - today.DayNumber)))
        {
            try
            {
                var page = await session.Navigate.ToRaceListAsync(candidate.Date, candidate.Course, cancellationToken);
                Assert.IsInstanceOfType<JraRaceListPage>(page);
                var list = (JraRaceListPage)page;
                Assert.AreEqual(candidate.Date, list.Date);
                Assert.AreEqual(candidate.Course, list.Course);
                Assert.IsTrue(list.Races.Count > 0, "公開済み開催のレース一覧が空でした。");
                return list;
            }
            catch (JraNavigationException ex) when (ex.Reason is
                JraNavigationFailureReason.NotYetPublished or JraNavigationFailureReason.OutOfDisplayedRange)
            {
                // 出馬表の掲載期間はカレンダーの開催予定とは異なる。
            }
        }

        Assert.Inconclusive("探索期間内に公開済みの出馬表がありません。取得成功の検証は未実施です。");
        throw new InvalidOperationException("Unreachable");
    }
}
