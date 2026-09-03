using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Scraping.Tests.TestSupport;

/// <summary>
/// Workflow層のテスト用フェイク。<see cref="ToCalendarAsync"/> の戻り値のみを
/// 設定でき、他のメソッドは呼ばれた場合に失敗させる（Workflow層が使わないことの検証を兼ねる）。
/// </summary>
internal sealed class FakeJraNavigator : IJraNavigator
{
    private readonly IJraPage _calendarResult;

    public FakeJraNavigator(IJraPage calendarResult)
    {
        _calendarResult = calendarResult;
    }

    public List<YearMonth> RequestedMonths { get; } = [];

    public Task<IJraPage> ToKeibaTopAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IJraPage> ToCalendarAsync(YearMonth month, CancellationToken cancellationToken = default)
    {
        RequestedMonths.Add(month);
        return Task.FromResult(_calendarResult);
    }

    public Task<IJraPage> ToRaceListAsync(DateOnly date, RaceCourse course, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IJraPage> ToRaceCardAsync(RaceId race, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IJraPage> ToRaceResultAsync(RaceId race, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IJraPage> ToHistoricalRaceSearchAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}
