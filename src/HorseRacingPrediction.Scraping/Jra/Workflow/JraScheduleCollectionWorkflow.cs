using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Scraping.Jra.Workflow;

/// <summary>
/// <see cref="IJraScheduleCollectionWorkflow"/> の実装。
/// オーケストレーションのみを行い、HTML解析やページ遷移の詳細は
/// <see cref="JraSession.Navigate"/>（Navigator/Parser層）に委譲する。
/// </summary>
public sealed class JraScheduleCollectionWorkflow
    : IJraScheduleCollectionWorkflow
{
    private readonly JraSession _session;

    public JraScheduleCollectionWorkflow(
        JraSession session)
    {
        _session = session;
    }

    public async Task<IReadOnlyList<RaceCourse>> CollectAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var month = new YearMonth(date.Year, date.Month);

        var page = await _session.Navigate.ToCalendarAsync(
            month,
            cancellationToken);

        if (page is not JraCalendarPage calendar)
        {
            throw new JraCollectionException(
                $"カレンダーページを取得できませんでした。 Kind={page.Kind}, Url={page.Url}");
        }

        var raceDate = calendar.RaceDates
            .FirstOrDefault(x => x.Date == date);

        return raceDate?.Courses ?? [];
    }
}
